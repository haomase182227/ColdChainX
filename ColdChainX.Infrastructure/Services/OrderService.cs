using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Infrastructure.Services
{
    public class OrderService : IOrderService
    {
        private const string PendingReview = "PENDING_REVIEW";
        private const string Rejected = "REJECTED";
        private const string Quoting = "QUOTING";
        private const decimal KgUnitPrice = 9500m;
        private const decimal CbmUnitPrice = 2800000m;
        private const decimal LastMileFreeKm = 10m;
        private const decimal LastMileUnitPrice = 15000m;
        private const decimal VatRate = 0.08m;

        private readonly ApplicationDbContext _db;
        private readonly ILocationService _locationService;
        private readonly IFileService _fileService;
        private readonly IHubContext<NotificationHub> _hubContext;

        public OrderService(
            ApplicationDbContext db,
            ILocationService locationService,
            IFileService fileService,
            IHubContext<NotificationHub> hubContext)
        {
            _db = db;
            _locationService = locationService;
            _fileService = fileService;
            _hubContext = hubContext;
        }

        public async Task<ApiResponse<IReadOnlyCollection<OrderResponse>>> GetOrdersAsync()
        {
            var orders = await BuildOrderQuery()
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => ToOrderResponse(o))
                .ToListAsync();

            return ApiResponse<IReadOnlyCollection<OrderResponse>>.SuccessResponse(orders, "Orders retrieved successfully");
        }

        public async Task<ApiResponse<OrderResponse>> GetOrderByIdAsync(Guid orderId)
        {
            var order = await BuildOrderQuery()
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return ApiResponse<OrderResponse>.Failure("Order not found");

            return ApiResponse<OrderResponse>.SuccessResponse(ToOrderResponse(order), "Order retrieved successfully");
        }

        public async Task<ApiResponse<IReadOnlyCollection<OrderResponse>>> GetOrdersByCustomerAsync(Guid customerId)
        {
            var customerExists = await _db.Customers.AnyAsync(c => c.CustomerId == customerId);
            if (!customerExists)
                return ApiResponse<IReadOnlyCollection<OrderResponse>>.Failure("Customer not found");

            var orders = await BuildOrderQuery()
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => ToOrderResponse(o))
                .ToListAsync();

            return ApiResponse<IReadOnlyCollection<OrderResponse>>.SuccessResponse(orders, "Customer orders retrieved successfully");
        }

        public async Task<ApiResponse<CreateOrderResponse>> CreateOrderAsync(CreateOrderRequest request)
        {
            if (request.DocumentImage == null || request.DocumentImage.Length == 0)
                return ApiResponse<CreateOrderResponse>.Failure("DocumentImage is required");

            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                var customerExists = await _db.Customers.AnyAsync(c => c.CustomerId == request.CustomerId);
                if (!customerExists)
                    return ApiResponse<CreateOrderResponse>.Failure("Customer not found");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var expectedCbm = Math.Round(request.LengthCm * request.WidthCm * request.HeightCm / 1000000m, 4);
                var coordinates = await _locationService.GetCoordinatesAsync(request.DestAddressText);

                var location = new Location
                {
                    LocationId = Guid.NewGuid(),
                    CustomerId = request.CustomerId,
                    LocationName = request.DestAddressText.Trim(),
                    Address = request.DestAddressText.Trim(),
                    Latitude = coordinates.Latitude,
                    Longitude = coordinates.Longitude,
                    Status = "ACTIVE",
                    CreatedAt = DbNow()
                };
                _db.Locations.Add(location);

                var order = new TransportOrder
                {
                    OrderId = Guid.NewGuid(),
                    TrackingCode = GenerateTrackingCode(),
                    CustomerId = request.CustomerId,
                    ItemName = request.ItemName.Trim(),
                    Category = request.Category.Trim(),
                    TempCondition = request.TempCondition.Trim(),
                    ExpectedWeightKg = request.ExpectedWeightKg,
                    ActualWeightKg = request.ExpectedWeightKg,
                    ExpectedCbm = expectedCbm,
                    DestLocation = location.LocationId,
                    CargoValue = 0,
                    Status = PendingReview,
                    CreatedAt = DbNow()
                };
                _db.TransportOrders.Add(order);

                var documentUrl = await _fileService.UploadFileAsync(request.DocumentImage);
                var uploadedBy = request.CustomerUserId ?? await ResolveCustomerUserIdAsync(request.CustomerId);
                if (!uploadedBy.HasValue)
                    return ApiResponse<CreateOrderResponse>.Failure("Customer user was not found for document upload");

                _db.TransportDocuments.Add(new TransportDocument
                {
                    DocId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    DocType = "ITEM_IMAGE",
                    ImageUrl = documentUrl,
                    Status = "PENDING",
                    UploadedBy = uploadedBy.Value,
                    CreatedAt = DbNow()
                });

                var salesUserId = await ResolveSalesUserIdAsync();
                await AddNotificationAsync(
                    salesUserId,
                    null,
                    "NOTI_ORDER_NEW",
                    order.OrderId,
                    new { Tracking_Code = order.TrackingCode });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _hubContext.Clients.Group("Group_Sales").SendAsync("OrderCreated", new
                {
                    order.OrderId,
                    order.TrackingCode,
                    order.CustomerId,
                    order.Status
                });

                return ApiResponse<CreateOrderResponse>.SuccessResponse(new CreateOrderResponse
                {
                    OrderId = order.OrderId,
                    TrackingCode = order.TrackingCode,
                    DestLocationId = location.LocationId,
                    ExpectedCbm = expectedCbm,
                    DocumentUrl = documentUrl,
                    Status = order.Status
                }, "Order created successfully");
            });
        }

        public async Task<ApiResponse<ReviewOrderResponse>> ReviewOrderAsync(Guid orderId, ReviewOrderRequest request)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                var order = await _db.TransportOrders
                    .Include(o => o.Customer)
                    .Include(o => o.DestLocationNavigation)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order == null)
                    return ApiResponse<ReviewOrderResponse>.Failure("Order not found");

                if (!string.Equals(order.Status, PendingReview, StringComparison.OrdinalIgnoreCase))
                    return ApiResponse<ReviewOrderResponse>.Failure("Order is not pending review");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var action = request.Action.Trim().ToUpperInvariant();
                if (action == "REJECT")
                {
                    if (string.IsNullOrWhiteSpace(request.RejectReason))
                        return ApiResponse<ReviewOrderResponse>.Failure("RejectReason is required when action is REJECT");

                    order.Status = Rejected;

                    var customerUserId = await ResolveCustomerUserIdAsync(order.CustomerId);
                    await AddNotificationAsync(
                        customerUserId,
                        request.SalesUserId,
                        "NOTI_ORDER_REJECTED",
                        order.OrderId,
                        new { Tracking_Code = order.TrackingCode, Reject_Reason = request.RejectReason });

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await _hubContext.Clients.User(order.CustomerId.ToString()!).SendAsync("OrderRejected", new
                    {
                        order.OrderId,
                        order.TrackingCode,
                        RejectReason = request.RejectReason
                    });

                    return ApiResponse<ReviewOrderResponse>.SuccessResponse(new ReviewOrderResponse
                    {
                        OrderId = order.OrderId,
                        TrackingCode = order.TrackingCode,
                        Status = order.Status
                    }, "Order rejected");
                }

                if (action != "APPROVE")
                    return ApiResponse<ReviewOrderResponse>.Failure("Action must be APPROVE or REJECT");

                if (order.DestLocationNavigation == null)
                    return ApiResponse<ReviewOrderResponse>.Failure("Order destination location was not found");

                var baseFreight = Math.Max(order.ExpectedWeightKg * KgUnitPrice, order.ExpectedCbm * CbmUnitPrice);
                var distanceKm = _locationService.CalculateDistance(
                    MockLocationService.HubLat,
                    MockLocationService.HubLon,
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
                _db.Quotations.Add(quotation);
                order.Status = Quoting;

                var quoteCustomerUserId = await ResolveCustomerUserIdAsync(order.CustomerId);
                await AddNotificationAsync(
                    quoteCustomerUserId,
                    request.SalesUserId,
                    "NOTI_QUOTATION_SENT",
                    order.OrderId,
                    new { Tracking_Code = order.TrackingCode, Final_Amount = finalAmount.ToString("0") });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _hubContext.Clients.User(order.CustomerId.ToString()!).SendAsync("ReceiveQuotation", new
                {
                    order.OrderId,
                    quotation.QuoteId,
                    order.TrackingCode,
                    quotation.FinalAmount
                });

                return ApiResponse<ReviewOrderResponse>.SuccessResponse(new ReviewOrderResponse
                {
                    OrderId = order.OrderId,
                    TrackingCode = order.TrackingCode,
                    Status = order.Status,
                    QuoteId = quotation.QuoteId,
                    BaseFreight = baseFreight,
                    LastMileSurcharge = lastMileSurcharge,
                    VasAmount = request.VasAmount,
                    VatAmount = vatAmount,
                    FinalAmount = finalAmount
                }, "Quotation generated");
            });
        }

        private async Task<Guid?> ResolveCustomerUserIdAsync(Guid? customerId)
        {
            if (!customerId.HasValue)
                return null;

            var customerEmail = await _db.Customers
                .Where(c => c.CustomerId == customerId.Value)
                .Select(c => c.Email)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(customerEmail))
                return null;

            return await _db.Users
                .Where(u => u.Email != null && u.Email.ToLower() == customerEmail.ToLower())
                .Select(u => (Guid?)u.UserId)
                .FirstOrDefaultAsync();
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
                CreatedAt = DbNow()
            });
        }

        private static DateTime DbNow()
            => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        private static string GenerateTrackingCode()
            => $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

        private IQueryable<TransportOrder> BuildOrderQuery()
        {
            return _db.TransportOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.DestLocationNavigation)
                .Include(o => o.TransportDocuments)
                .Include(o => o.Quotations);
        }

        private static OrderResponse ToOrderResponse(TransportOrder order)
        {
            return new OrderResponse
            {
                OrderId = order.OrderId,
                TrackingCode = order.TrackingCode,
                CustomerId = order.CustomerId,
                CustomerName = order.Customer?.CompanyName,
                ItemName = order.ItemName,
                Category = order.Category,
                TempCondition = order.TempCondition,
                ExpectedWeightKg = order.ExpectedWeightKg,
                ActualWeightKg = order.ActualWeightKg,
                ExpectedCbm = order.ExpectedCbm,
                ActualCbm = order.ActualCbm,
                CargoValue = order.CargoValue,
                Status = order.Status,
                CreatedAt = order.CreatedAt,
                Destination = order.DestLocationNavigation == null
                    ? null
                    : new OrderLocationResponse
                    {
                        LocationId = order.DestLocationNavigation.LocationId,
                        LocationName = order.DestLocationNavigation.LocationName,
                        Address = order.DestLocationNavigation.Address,
                        Latitude = order.DestLocationNavigation.Latitude,
                        Longitude = order.DestLocationNavigation.Longitude
                    },
                Documents = order.TransportDocuments
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => new OrderDocumentResponse
                    {
                        DocId = d.DocId,
                        DocType = d.DocType,
                        ImageUrl = d.ImageUrl,
                        Status = d.Status,
                        CreatedAt = d.CreatedAt
                    })
                    .ToList(),
                Quotations = order.Quotations
                    .OrderByDescending(q => q.CreatedAt)
                    .Select(q => new OrderQuotationResponse
                    {
                        QuoteId = q.QuoteId,
                        BaseFreight = q.BaseFreight,
                        LastMileSurcharge = q.LastMileSurcharge,
                        VasAmount = q.VasAmount,
                        VatAmount = q.VatAmount,
                        FinalAmount = q.FinalAmount,
                        Status = q.Status,
                        CreatedAt = q.CreatedAt
                    })
                    .ToList()
            };
        }
    }
}
