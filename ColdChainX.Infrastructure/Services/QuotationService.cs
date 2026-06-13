using System.Text.Json;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Quotations;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Infrastructure.Services
{
    public class QuotationService : IQuotationService
    {
        private const string Quoting = "QUOTING";
        private const string DefaultOriginCity = "Ho Chi Minh";
        private const decimal MinCharge = 300000m;
        private const decimal LastMileFreeKm = 10m;
        private const decimal LastMileUnitPrice = 15000m;
        private const decimal VatRate = 0.08m;

        private readonly ApplicationDbContext _db;
        private readonly ILocationService _locationService;
        private readonly IPdfService _pdfService;
        private readonly IWebHostEnvironment _environment;
        private readonly IHubContext<NotificationHub> _hubContext;

        public QuotationService(
            ApplicationDbContext db,
            ILocationService locationService,
            IPdfService pdfService,
            IWebHostEnvironment environment,
            IHubContext<NotificationHub> hubContext)
        {
            _db = db;
            _locationService = locationService;
            _pdfService = pdfService;
            _environment = environment;
            _hubContext = hubContext;
        }

        public async Task<ApiResponse<PagedResult<QuotationResponse>>> GetQuotationsAsync(int pageNumber, int pageSize)
        {
            var query = BuildQuotationQuery().OrderByDescending(q => q.CreatedAt);
            var totalRecords = await query.CountAsync();
            var data = await query
                .Skip(NormalizeSkip(pageNumber, pageSize))
                .Take(NormalizePageSize(pageSize))
                .Select(q => ToQuotationResponse(q))
                .ToListAsync();

            return ApiResponse<PagedResult<QuotationResponse>>.SuccessResponse(
                PagedResult<QuotationResponse>.Create(data, totalRecords, pageNumber, NormalizePageSize(pageSize)),
                "Quotations retrieved successfully");
        }

        public async Task<ApiResponse<QuotationResponse>> GetQuotationByIdAsync(Guid quoteId)
        {
            var quotation = await BuildQuotationQuery()
                .FirstOrDefaultAsync(q => q.QuoteId == quoteId);

            if (quotation == null)
                return ApiResponse<QuotationResponse>.Failure("Quotation not found");

            return ApiResponse<QuotationResponse>.SuccessResponse(ToQuotationResponse(quotation), "Quotation retrieved successfully");
        }

        public async Task<ApiResponse<PagedResult<QuotationResponse>>> GetQuotationsByOrderAsync(Guid orderId, int pageNumber, int pageSize)
        {
            var orderExists = await _db.TransportOrders.AnyAsync(o => o.OrderId == orderId);
            if (!orderExists)
                return ApiResponse<PagedResult<QuotationResponse>>.Failure("Order not found");

            var query = BuildQuotationQuery()
                .Where(q => q.OrderId == orderId)
                .OrderByDescending(q => q.CreatedAt);
            var totalRecords = await query.CountAsync();
            var data = await query
                .Skip(NormalizeSkip(pageNumber, pageSize))
                .Take(NormalizePageSize(pageSize))
                .Select(q => ToQuotationResponse(q))
                .ToListAsync();

            return ApiResponse<PagedResult<QuotationResponse>>.SuccessResponse(
                PagedResult<QuotationResponse>.Create(data, totalRecords, pageNumber, NormalizePageSize(pageSize)),
                "Order quotations retrieved successfully");
        }

        public async Task<ApiResponse<QuotationResponse>> CreateQuotationAsync(CreateQuotationRequest request, Guid salesUserId)
        {
            if (request.OrderId == Guid.Empty)
                return ApiResponse<QuotationResponse>.Failure("OrderId is required");

            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                var order = await _db.TransportOrders
                    .Include(o => o.Customer)
                    .Include(o => o.DestLocationNavigation)
                    .FirstOrDefaultAsync(o => o.OrderId == request.OrderId);

                if (order == null)
                    return ApiResponse<QuotationResponse>.Failure("Order not found");

                if (order.DestLocationNavigation == null)
                    return ApiResponse<QuotationResponse>.Failure("Order destination location was not found");

                if (!order.CustomerId.HasValue)
                    return ApiResponse<QuotationResponse>.Failure("Order customer was not found");

                var hasOpenQuotation = await _db.Quotations.AnyAsync(q =>
                    q.OrderId == order.OrderId
                    && (q.Status == "SENT" || q.Status == "ACCEPTED"));
                if (hasOpenQuotation)
                    return ApiResponse<QuotationResponse>.Failure("Order already has an active quotation");

                var pricing = await ResolvePricingAsync(order);
                if (pricing == null)
                    return ApiResponse<QuotationResponse>.Failure("Pricing matrix is missing KG or CBM price for this route");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var chargeableWeightKg = order.ActualWeightKg;
                var chargeableCbm = order.ExpectedCbm;
                var baseFreight = Math.Max(chargeableWeightKg * pricing.PriceKg, chargeableCbm * pricing.PriceCbm);
                if (baseFreight < MinCharge)
                    baseFreight = MinCharge;

                var distanceKm = await _locationService.GetDistanceKmAsync(
                    GoongLocationService.HubLat,
                    GoongLocationService.HubLon,
                    order.DestLocationNavigation.Latitude,
                    order.DestLocationNavigation.Longitude);
                var lastMileSurcharge = distanceKm > LastMileFreeKm
                    ? Math.Round((distanceKm - LastMileFreeKm) * LastMileUnitPrice, 0)
                    : 0m;
                var subtotal = baseFreight + lastMileSurcharge + request.VasAmount;
                var vatAmount = Math.Round(subtotal * VatRate, 0);
                var finalAmount = subtotal + vatAmount;

                var quotation = new Quotation
                {
                    QuoteId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    BaseFreight = baseFreight,
                    LastMileSurcharge = lastMileSurcharge,
                    VasAmount = request.VasAmount,
                    VatAmount = vatAmount,
                    FinalAmount = finalAmount,
                    Status = "SENT",
                    CreatedAt = DbNow()
                };
                quotation.FileUrl = await GenerateQuotationPdfAsync(order, quotation);
                _db.Quotations.Add(quotation);
                order.Status = Quoting;

                var customerUserId = await ResolveCustomerUserIdAsync(order.CustomerId.Value);
                await AddNotificationAsync(
                    customerUserId,
                    salesUserId,
                    "NOTI_QUOTATION_SENT",
                    order.OrderId,
                    new { Tracking_Code = order.TrackingCode, Final_Amount = finalAmount.ToString("0") });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _hubContext.Clients.User(order.CustomerId.Value.ToString()).SendAsync("ReceiveQuotation", new
                {
                    order.OrderId,
                    quotation.QuoteId,
                    order.TrackingCode,
                    quotation.FinalAmount,
                    quotation.FileUrl
                });

                quotation.Order = order;
                return ApiResponse<QuotationResponse>.SuccessResponse(ToQuotationResponse(quotation), "Quotation created successfully");
            });
        }

        public async Task<ApiResponse<AcceptQuotationResponse>> AcceptQuotationAsync(Guid quoteId, AcceptQuotationRequest request)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                var quotation = await _db.Quotations
                    .Include(q => q.Order)
                    .FirstOrDefaultAsync(q => q.QuoteId == quoteId);

                if (quotation == null)
                    return ApiResponse<AcceptQuotationResponse>.Failure("Quotation not found");

                if (!string.Equals(quotation.Status, "SENT", StringComparison.OrdinalIgnoreCase))
                    return ApiResponse<AcceptQuotationResponse>.Failure("Quotation is not available for acceptance");

                if (quotation.Order == null)
                    return ApiResponse<AcceptQuotationResponse>.Failure("Quotation order was not found");

                if (quotation.Order.CustomerId != request.CustomerId)
                    return ApiResponse<AcceptQuotationResponse>.Failure("Customer_ID does not match quotation order");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                quotation.Status = "ACCEPTED";
                quotation.Order.Status = "CONTRACT_PENDING";

                var salesUserId = await ResolveSalesUserIdAsync();
                var customerUserId = await ResolveCustomerUserIdAsync(request.CustomerId);
                await AddNotificationAsync(
                    salesUserId,
                    customerUserId,
                    "NOTI_QUOTATION_ACCEPTED",
                    quotation.Order.OrderId,
                    new { Tracking_Code = quotation.Order.TrackingCode });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _hubContext.Clients.Group("Group_Sales").SendAsync("QuoteAccepted", new
                {
                    quotation.QuoteId,
                    quotation.Order.OrderId,
                    quotation.Order.TrackingCode
                });

                return ApiResponse<AcceptQuotationResponse>.SuccessResponse(new AcceptQuotationResponse
                {
                    QuoteId = quotation.QuoteId,
                    OrderId = quotation.Order.OrderId,
                    TrackingCode = quotation.Order.TrackingCode,
                    FileUrl = quotation.FileUrl,
                    QuoteStatus = quotation.Status,
                    OrderStatus = quotation.Order.Status
                }, "Quotation accepted");
            });
        }

        private async Task<Guid?> ResolveSalesUserIdAsync()
        {
            return await _db.Users
                .Include(u => u.Role)
                .Where(u => u.Role != null
                            && (u.Role.RoleName.ToLower() == "sales"
                                || u.Role.RoleName.ToLower() == "admin"
                                || u.Role.RoleName.ToLower() == "manager"))
                .Select(u => (Guid?)u.UserId)
                .FirstOrDefaultAsync();
        }

        private async Task<Guid?> ResolveCustomerUserIdAsync(Guid customerId)
        {
            var customerEmail = await _db.Customers
                .Where(c => c.CustomerId == customerId)
                .Select(c => c.Email)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(customerEmail))
                return null;

            return await _db.Users
                .Where(u => u.Email != null && u.Email.ToLower() == customerEmail.ToLower())
                .Select(u => (Guid?)u.UserId)
                .FirstOrDefaultAsync();
        }

        private async Task<RoutePricing?> ResolvePricingAsync(TransportOrder order)
        {
            var destinationAddress = order.DestLocationNavigation?.Address ?? string.Empty;
            var destinationCity = ExtractDestinationCity(destinationAddress);

            var allRoutePrices = await _db.PricingMatrices
                .AsNoTracking()
                .Where(p => p.PricingUnit == "KG" || p.PricingUnit == "CBM")
                .ToListAsync();

            var originKey = NormalizeRouteKey(DefaultOriginCity);
            var destinationKey = NormalizeRouteKey(destinationCity);
            var prices = allRoutePrices
                .Where(p => NormalizeRouteKey(p.OriginCity) == originKey
                            && NormalizeRouteKey(p.DestCity) == destinationKey)
                .OrderByDescending(p => p.EffectiveDate)
                .ToList();

            var priceKg = prices.FirstOrDefault(p => p.PricingUnit == "KG")?.UnitPrice;
            var priceCbm = prices.FirstOrDefault(p => p.PricingUnit == "CBM")?.UnitPrice;

            if (!priceKg.HasValue || !priceCbm.HasValue)
                return null;

            return new RoutePricing(priceKg.Value, priceCbm.Value);
        }

        private async Task<string> GenerateQuotationPdfAsync(TransportOrder order, Quotation quotation)
        {
            var templatePath = Path.Combine(_environment.ContentRootPath, "Templates", "QuotationTemplate.html");
            if (!File.Exists(templatePath))
                throw new InvalidOperationException("QuotationTemplate.html was not found");

            var html = await File.ReadAllTextAsync(templatePath);
            var replacements = new Dictionary<string, string?>
            {
                ["Quote_Date"] = DateTime.UtcNow.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                ["Customer_CompanyName"] = order.Customer?.CompanyName ?? string.Empty,
                ["Tracking_Code"] = order.TrackingCode,
                ["Item_Name"] = order.ItemName,
                ["Quantity"] = order.Quantity.ToString(CultureInfo.InvariantCulture),
                ["Packing_Type"] = order.PackingType,
                ["Pickup_Address"] = DefaultOriginCity,
                ["Dest_Address"] = order.DestLocationNavigation?.Address ?? string.Empty,
                ["Actual_Weight_KG"] = order.ActualWeightKg.ToString("0.##", CultureInfo.InvariantCulture),
                ["Actual_CBM"] = order.ExpectedCbm.ToString("0.####", CultureInfo.InvariantCulture),
                ["Final_Amount"] = quotation.FinalAmount.ToString("N0", CultureInfo.InvariantCulture)
            };

            foreach (var replacement in replacements)
                html = html.Replace($"{{{{{replacement.Key}}}}}", replacement.Value ?? string.Empty);

            return await _pdfService.SaveQuotationPdfAsync(html, $"QUO-{quotation.QuoteId:N}");
        }

        private static string ExtractDestinationCity(string address)
        {
            var normalized = RemoveDiacritics(address).ToLowerInvariant();

            if (normalized.Contains("ha noi")) return "Ha Noi";
            if (normalized.Contains("da nang")) return "Da Nang";
            if (normalized.Contains("can tho")) return "Can Tho";
            if (normalized.Contains("kien giang")) return "Kien Giang";
            if (normalized.Contains("dong nai")) return "Dong Nai";
            if (normalized.Contains("binh duong")) return "Binh Duong";
            if (normalized.Contains("ho chi minh") || normalized.Contains("hcm") || normalized.Contains("tp.hcm") || normalized.Contains("sai gon")) return "Ho Chi Minh";

            return "Ho Chi Minh";
        }

        private static string NormalizeRouteKey(string? value)
        {
            return RemoveDiacritics(value ?? string.Empty)
                .ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace(".", string.Empty)
                .Replace("-", string.Empty);
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(ch);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private async Task AddNotificationAsync(Guid? userId, Guid? senderId, string templateId, Guid orderId, object parameters)
        {
            if (!userId.HasValue)
                return;

            var templateExists = await _db.NotificationTemplates.AnyAsync(t => t.TemplateId == templateId);
            if (!templateExists)
                return;

            _db.Notifications.Add(new Notification
            {
                NotiId = Guid.NewGuid(),
                UserId = userId.Value,
                SenderId = senderId,
                TemplateId = templateId,
                OrderId = orderId,
                Params = JsonSerializer.Serialize(parameters),
                IsRead = false,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
        }

        private IQueryable<Quotation> BuildQuotationQuery()
        {
            return _db.Quotations
                .AsNoTracking()
                .Include(q => q.Order)
                    .ThenInclude(o => o!.Customer);
        }

        private static QuotationResponse ToQuotationResponse(Quotation quotation)
        {
            return new QuotationResponse
            {
                QuoteId = quotation.QuoteId,
                OrderId = quotation.OrderId,
                TrackingCode = quotation.Order?.TrackingCode,
                CustomerId = quotation.Order?.CustomerId,
                CustomerName = quotation.Order?.Customer?.CompanyName,
                BaseFreight = quotation.BaseFreight,
                LastMileSurcharge = quotation.LastMileSurcharge,
                VasAmount = quotation.VasAmount,
                VatAmount = quotation.VatAmount,
                FinalAmount = quotation.FinalAmount,
                FileUrl = quotation.FileUrl,
                Status = quotation.Status,
                CreatedAt = quotation.CreatedAt
            };
        }

        private static DateTime DbNow()
        {
            return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        }

        private static int NormalizePageSize(int pageSize)
            => Math.Clamp(pageSize <= 0 ? 10 : pageSize, 1, 100);

        private static int NormalizeSkip(int pageNumber, int pageSize)
        {
            var safePageNumber = pageNumber <= 0 ? 1 : pageNumber;
            return (safePageNumber - 1) * NormalizePageSize(pageSize);
        }

        private sealed record RoutePricing(decimal PriceKg, decimal PriceCbm);
    }
}
