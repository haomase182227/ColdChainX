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
        private const string Draft = "DRAFT";
        private const string Sent = "SENT";
        private const string DefaultOriginCity = "HCM";
        private const decimal MinChargeableWeightKg = 30m;
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

        public async Task<ApiResponse<PagedResult<QuotationResponse>>> GetQuotationsByCustomerAsync(Guid customerId, int pageNumber, int pageSize)
        {
            var query = BuildQuotationQuery()
                .Where(q => q.Order != null && q.Order.CustomerId == customerId)
                .OrderByDescending(q => q.CreatedAt);
            var totalRecords = await query.CountAsync();
            var data = await query
                .Skip(NormalizeSkip(pageNumber, pageSize))
                .Take(NormalizePageSize(pageSize))
                .Select(q => ToQuotationResponse(q))
                .ToListAsync();

            return ApiResponse<PagedResult<QuotationResponse>>.SuccessResponse(
                PagedResult<QuotationResponse>.Create(data, totalRecords, pageNumber, NormalizePageSize(pageSize)),
                "Customer quotations retrieved successfully");
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
                    .Include(o => o.Schedule)
                        .ThenInclude(s => s!.Route)
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
                    return ApiResponse<QuotationResponse>.Failure("Weight tier is missing for this route");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var quotation = await BuildAutoDraftQuotationAsync(order, order.Schedule?.Route!, order.DestLocationNavigation);
                _db.Quotations.Add(quotation);

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                quotation.Order = order;
                return ApiResponse<QuotationResponse>.SuccessResponse(ToQuotationResponse(quotation), "Draft quotation created successfully");
            });
        }

        public async Task<ApiResponse<QuotationResponse>> GenerateAutoQuotationAsync(Guid orderId)
        {
            var order = await _db.TransportOrders
                .Include(o => o.Customer)
                .Include(o => o.Schedule)
                    .ThenInclude(s => s!.Route)
                .Include(o => o.DestLocationNavigation)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return ApiResponse<QuotationResponse>.Failure("Order not found");
            if (order.Schedule?.Route == null)
                return ApiResponse<QuotationResponse>.Failure("Order has no selected route");
            if (order.DestLocationNavigation == null)
                return ApiResponse<QuotationResponse>.Failure("Order destination location was not found");

            var quotation = await BuildAutoDraftQuotationAsync(order, order.Schedule?.Route, order.DestLocationNavigation);
            _db.Quotations.Add(quotation);
            await _db.SaveChangesAsync();

            quotation.Order = order;
            return ApiResponse<QuotationResponse>.SuccessResponse(ToQuotationResponse(quotation), "Draft quotation generated");
        }

        public async Task<ApiResponse<QuotationResponse>> EditQuotationAsync(Guid quoteId, EditQuotationRequest request, Guid salesUserId)
        {
            var quotation = await _db.Quotations
                .Include(q => q.Order)
                    .ThenInclude(o => o!.Customer)
                .FirstOrDefaultAsync(q => q.QuoteId == quoteId);

            if (quotation == null)
                return ApiResponse<QuotationResponse>.Failure("Quotation not found");
            if (!string.Equals(quotation.Status, Draft, StringComparison.OrdinalIgnoreCase))
                return ApiResponse<QuotationResponse>.Failure("Only DRAFT quotations can be edited");
            if ((request.BaseFreight.HasValue && request.BaseFreight.Value < 0)
                || (request.LastMileSurcharge.HasValue && request.LastMileSurcharge.Value < 0))
                return ApiResponse<QuotationResponse>.Failure("Freight values cannot be negative");
            if (request.VatPercentage.HasValue && (request.VatPercentage.Value < 0 || request.VatPercentage.Value > 20))
                return ApiResponse<QuotationResponse>.Failure("VAT percentage is invalid");
            var additionalCharges = request.AdditionalCharges == null
                ? DeserializeAdditionalCharges(quotation.AdditionalCharges)
                : NormalizeAdditionalCharges(request.AdditionalCharges);
            if (additionalCharges == null)
                return ApiResponse<QuotationResponse>.Failure("Additional charge name is required and amount cannot be negative");

            var baseFreight = request.BaseFreight ?? quotation.BaseFreight;
            var lastMileSurcharge = request.LastMileSurcharge ?? quotation.LastMileSurcharge ?? 0m;
            var vatPercentage = request.VatPercentage ?? quotation.VatPercentage ?? 8m;
            var additionalChargesTotal = additionalCharges.Sum(c => c.Amount);
            var subtotal = baseFreight + lastMileSurcharge + additionalChargesTotal;
            quotation.BaseFreight = baseFreight;
            quotation.LastMileSurcharge = lastMileSurcharge;
            quotation.AdditionalCharges = SerializeAdditionalCharges(additionalCharges);
            quotation.VasAmount = 0m;
            quotation.VatPercentage = vatPercentage;
            quotation.VatAmount = Math.Round(subtotal * vatPercentage / 100m, 0);
            quotation.FinalAmount = subtotal + quotation.VatAmount;
            quotation.ManualAdjustment = quotation.BaseFreight - (quotation.SystemBaseFreight ?? 0m) + additionalChargesTotal;
            if (request.OverrideReason != null)
                quotation.OverrideReason = string.IsNullOrWhiteSpace(request.OverrideReason) ? null : request.OverrideReason.Trim();
            quotation.PricingSource = "MANUAL_OVERRIDE";

            await _db.SaveChangesAsync();
            return ApiResponse<QuotationResponse>.SuccessResponse(ToQuotationResponse(quotation), "Quotation updated");
        }

        public async Task<ApiResponse<QuotationResponse>> SendQuotationAsync(Guid quoteId, Guid salesUserId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var quotation = await _db.Quotations
                    .Include(q => q.Order)
                        .ThenInclude(o => o!.Customer)
                    .Include(q => q.Order)
                        .ThenInclude(o => o!.Schedule).ThenInclude(s => s!.Route)
                    .Include(q => q.Order)
                        .ThenInclude(o => o!.DestLocationNavigation)
                    .FirstOrDefaultAsync(q => q.QuoteId == quoteId);

                if (quotation == null)
                    return ApiResponse<QuotationResponse>.Failure("Quotation not found");
                if (!string.Equals(quotation.Status, Draft, StringComparison.OrdinalIgnoreCase))
                    return ApiResponse<QuotationResponse>.Failure("Only DRAFT quotations can be sent");
                if (quotation.Order == null || !quotation.Order.CustomerId.HasValue)
                    return ApiResponse<QuotationResponse>.Failure("Quotation order/customer was not found");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                quotation.Status = Sent;
                quotation.FileUrl = await GenerateQuotationPdfAsync(quotation.Order, quotation);
                quotation.Order.Status = Quoting;

                var customerUserId = await ResolveCustomerUserIdAsync(quotation.Order.CustomerId.Value);
                await AddNotificationAsync(
                    customerUserId,
                    salesUserId,
                    "NOTI_QUOTATION_SENT",
                    quotation.Order.OrderId,
                    new { Tracking_Code = quotation.Order.TrackingCode, Final_Amount = quotation.FinalAmount.ToString("0") });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _hubContext.Clients.User(quotation.Order.CustomerId.Value.ToString()).SendAsync("ReceiveQuotation", new
                {
                    quotation.Order.OrderId,
                    quotation.QuoteId,
                    quotation.Order.TrackingCode,
                    quotation.FinalAmount,
                    quotation.FileUrl
                });

                return ApiResponse<QuotationResponse>.SuccessResponse(ToQuotationResponse(quotation), "Quotation sent");
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

                var hasContract = await _db.CustomerContracts.AnyAsync(c => c.OrderId == quotation.Order.OrderId);
                if (!hasContract)
                {
                    _db.CustomerContracts.Add(new CustomerContract
                    {
                        ContractId = Guid.NewGuid(),
                        CustomerId = quotation.Order.CustomerId,
                        OrderId = quotation.Order.OrderId,
                        ContractNumber = await GenerateUniqueContractNumberAsync(),
                        ExpiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                        FileUrl = string.Empty,
                        DraftHtmlContent = null,
                        Status = "DRAFT",
                        CreatedAt = DbNow()
                    });
                }

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

        private async Task<string> GenerateUniqueContractNumberAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                var value = $"HD-{DateTime.UtcNow:yyyy}{Random.Shared.Next(0, 999999):D6}";
                if (!await _db.CustomerContracts.AnyAsync(c => c.ContractNumber == value))
                    return value;
            }

            return $"HD-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        }

        private async Task<Quotation> BuildAutoDraftQuotationAsync(TransportOrder order, RouteMaster route, Location destination)
        {
            var volumetricRate = await GetSystemConfigDecimalAsync("VolumetricConversionRate", 250m);
            var pricePerKm = await GetSystemConfigDecimalAsync("PricePerKm", 15000m);

            var volumetricWeight = Math.Round((order.OrderDimension?.ExpectedCbm ?? 0m) * volumetricRate, 2);
            var chargeableWeight = Math.Max(Math.Max((order.OrderDimension?.ExpectedWeightKg ?? 0m), volumetricWeight), MinChargeableWeightKg);

            var tier = await _db.WeightTiers
                .AsNoTracking()
                .Where(t => t.RouteId == route.RouteId
                            && chargeableWeight >= t.MinWeightKg
                            && (!t.MaxWeightKg.HasValue || chargeableWeight <= t.MaxWeightKg.Value))
                .OrderByDescending(t => t.MinWeightKg)
                .FirstOrDefaultAsync();

            if (tier == null)
                throw new InvalidOperationException(BuildChargeableWeightErrorMessage(order, chargeableWeight, volumetricWeight));

            var routeDestinationCoordinates = await _locationService.GetCoordinatesAsync($"{route.DestCity}, Vietnam");
            var distanceKm = await _locationService.GetDistanceKmAsync(
                routeDestinationCoordinates.Latitude,
                routeDestinationCoordinates.Longitude,
                destination.Latitude,
                destination.Longitude);

            var baseFreight = Math.Round(chargeableWeight * tier.PricePerKg, 0);
            var lastMileSurcharge = Math.Round(distanceKm * pricePerKm, 0);
            var subtotal = baseFreight + lastMileSurcharge;
            var vatAmount = Math.Round(subtotal * VatRate, 0);

            return new Quotation
            {
                QuoteId = Guid.NewGuid(),
                OrderId = order.OrderId,
                BaseFreight = baseFreight,
                LastMileSurcharge = lastMileSurcharge,
                VasAmount = 0m,
                VatPercentage = VatRate * 100m,
                VatAmount = vatAmount,
                FinalAmount = subtotal + vatAmount,
                ChargeableWeightKg = chargeableWeight,
                VolumetricWeightKg = volumetricWeight,
                PricePerKg = tier.PricePerKg,
                DistanceKm = distanceKm,
                SystemBaseFreight = baseFreight,
                ManualAdjustment = 0m,
                OverrideReason = null,
                PricingSource = "AUTO",
                Status = Draft,
                CreatedAt = DbNow()
            };
        }

        private async Task<decimal> GetSystemConfigDecimalAsync(string key, decimal fallback)
        {
            var value = await _db.SystemConfigs
                .AsNoTracking()
                .Where(c => c.Key == key)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        private async Task<RoutePricing?> ResolvePricingAsync(TransportOrder order)
        {
            if (order.Schedule == null)
                return null;

            var volumetricRate = await GetSystemConfigDecimalAsync("VolumetricConversionRate", 250m);
            var volumetricWeight = Math.Round((order.OrderDimension?.ExpectedCbm ?? 0m) * volumetricRate, 2);
            var chargeableWeight = Math.Max(Math.Max((order.OrderDimension?.ExpectedWeightKg ?? 0m), volumetricWeight), MinChargeableWeightKg);

            var routeId = order.Schedule?.RouteId;
            var tier = await _db.WeightTiers
                .AsNoTracking()
                .Include(t => t.Route)
                .Where(t => t.RouteId == routeId
                            && chargeableWeight >= t.MinWeightKg
                            && (!t.MaxWeightKg.HasValue || chargeableWeight <= t.MaxWeightKg.Value))
                .OrderByDescending(t => t.MinWeightKg)
                .FirstOrDefaultAsync();

            if (tier == null)
                return null;

            var baseFreight = Math.Round(chargeableWeight * tier.PricePerKg, 0);

            return new RoutePricing(
                BaseFreight: baseFreight,
                PriceKg: tier.PricePerKg,
                FreightByKg: baseFreight,
                ChargeableWeightKg: chargeableWeight,
                OriginCity: tier.Route.OriginCity,
                DestinationCity: tier.Route.DestCity);
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
                ["Pickup_Address"] = order.Schedule?.Route?.OriginCity ?? DefaultOriginCity,
                ["Dest_Address"] = order.DestLocationNavigation?.Address ?? string.Empty,
                ["Actual_Weight_KG"] = (order.OrderDimension?.ActualWeightKg ?? 0m).ToString("0.##", CultureInfo.InvariantCulture),
                ["Actual_CBM"] = (order.OrderDimension?.ExpectedCbm ?? 0m).ToString("0.####", CultureInfo.InvariantCulture),
                ["Route_Code"] = order.Schedule?.Route?.RouteCode,
                ["ETD"] = string.Empty,
                ["ETA"] = order.Schedule?.Route?.TransitTime,
                ["Cut_Off_Time"] = order.Schedule?.Route?.CutOffTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                ["Base_Freight"] = quotation.BaseFreight.ToString("N0", CultureInfo.InvariantCulture),
                ["Final_Amount"] = quotation.BaseFreight.ToString("N0", CultureInfo.InvariantCulture)
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
                    .ThenInclude(o => o!.Customer)
                .Include(q => q.Order)
                    .ThenInclude(o => o!.Schedule).ThenInclude(s => s!.Route);
        }

        private static QuotationResponse ToQuotationResponse(Quotation quotation)
        {
            var additionalCharges = DeserializeAdditionalCharges(quotation.AdditionalCharges);

            return new QuotationResponse
            {
                QuoteId = quotation.QuoteId,
                OrderId = quotation.OrderId,
                TrackingCode = quotation.Order?.TrackingCode,
                CustomerId = quotation.Order?.CustomerId,
                CustomerName = quotation.Order?.Customer?.CompanyName,
                BaseFreight = quotation.BaseFreight,
                LastMileSurcharge = quotation.LastMileSurcharge,
                AdditionalCharges = additionalCharges
                    .Select(c => new QuotationAdditionalChargeResponse
                    {
                        Name = c.Name,
                        Amount = c.Amount,
                        Note = c.Note
                    })
                    .ToArray(),
                AdditionalChargesTotal = additionalCharges.Sum(c => c.Amount),
                VatPercentage = quotation.VatPercentage,
                VatAmount = quotation.VatAmount,
                FinalAmount = quotation.FinalAmount,
                ChargeableWeightKg = quotation.ChargeableWeightKg,
                VolumetricWeightKg = quotation.VolumetricWeightKg,
                PricePerKg = quotation.PricePerKg,
                DistanceKm = quotation.DistanceKm,
                SystemBaseFreight = quotation.SystemBaseFreight,
                ManualAdjustment = quotation.ManualAdjustment,
                OverrideReason = quotation.OverrideReason,
                PricingSource = quotation.PricingSource,
                FileUrl = quotation.FileUrl,
                Status = quotation.Status,
                CreatedAt = quotation.CreatedAt
            };
        }

        private static IReadOnlyCollection<QuotationAdditionalCharge>? NormalizeAdditionalCharges(
            IReadOnlyCollection<QuotationAdditionalChargeRequest>? charges)
        {
            if (charges == null || charges.Count == 0)
                return Array.Empty<QuotationAdditionalCharge>();

            var normalized = new List<QuotationAdditionalCharge>();
            foreach (var charge in charges)
            {
                var name = charge.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name) || charge.Amount < 0)
                    return null;

                normalized.Add(new QuotationAdditionalCharge(
                    name,
                    charge.Amount,
                    string.IsNullOrWhiteSpace(charge.Note) ? null : charge.Note.Trim()));
            }

            return normalized;
        }

        private static string? SerializeAdditionalCharges(IReadOnlyCollection<QuotationAdditionalCharge> charges)
        {
            return charges.Count == 0 ? null : JsonSerializer.Serialize(charges);
        }

        private static IReadOnlyCollection<QuotationAdditionalCharge> DeserializeAdditionalCharges(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<QuotationAdditionalCharge>();

            try
            {
                var charges = JsonSerializer.Deserialize<List<QuotationAdditionalCharge>>(value);
                if (charges == null)
                    return Array.Empty<QuotationAdditionalCharge>();

                return charges;
            }
            catch (JsonException)
            {
                return Array.Empty<QuotationAdditionalCharge>();
            }
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

        private sealed record RoutePricing(
            decimal BaseFreight,
            decimal PriceKg,
            decimal FreightByKg,
            decimal ChargeableWeightKg,
            string OriginCity,
            string DestinationCity);

        private sealed record QuotationAdditionalCharge(string Name, decimal Amount, string? Note);

        private static string BuildChargeableWeightErrorMessage(
            TransportOrder order,
            decimal chargeableWeight,
            decimal volumetricWeight)
        {
            return "Hệ thống phát hiện kích thước Dài x Rộng x Cao và số lượng của bạn quá lớn so với trọng lượng thực tế "
                   + $"({FormatKg((order.OrderDimension?.ExpectedWeightKg ?? 0m))}kg), dẫn đến trọng lượng quy đổi lên tới {FormatKg(volumetricWeight)}kg "
                   + $"và trọng lượng tính cước là {FormatKg(chargeableWeight)}kg. "
                   + "Bạn vui lòng kiểm tra lại đã nhập đúng kích thước theo đơn vị Centimet (CM) chưa nhé. "
                   + "Nếu kích thước bạn nhập là chính xác, đơn hàng này cần được vận chuyển theo hình thức Bao Nguyên Xe (FTL). "
                   + "Vui lòng liên hệ Hotline/Sales để được báo giá riêng.";
        }

        private static string FormatKg(decimal value)
        {
            return value.ToString("#,##0.##", CultureInfo.GetCultureInfo("vi-VN"));
        }
    }
}

