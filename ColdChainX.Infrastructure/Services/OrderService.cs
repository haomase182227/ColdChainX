using System.Text.Json;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using ColdChainX.Application.DTOs.Common;
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
        private const string Approved = "APPROVED";
        private const string Draft = "DRAFT";
        private const string DefaultOriginCity = "HCM";
        private const decimal MinChargeableWeightKg = 30m;
        private const decimal VatRate = 0.08m;

        private readonly ApplicationDbContext _db;
        private readonly ILocationService _locationService;
        private readonly IFileService _fileService;
        private readonly IPdfService _pdfService;
        private readonly IWebHostEnvironment _environment;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly INotificationService? _notificationService;
        private readonly ILogger<OrderService>? _logger;

        public OrderService(
            ApplicationDbContext db,
            ILocationService locationService,
            IFileService fileService,
            IPdfService pdfService,
            IWebHostEnvironment environment,
            IHubContext<NotificationHub> hubContext,
            INotificationService? notificationService = null,
            ILogger<OrderService>? logger = null)
        {
            _db = db;
            _locationService = locationService;
            _fileService = fileService;
            _pdfService = pdfService;
            _environment = environment;
            _hubContext = hubContext;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<ApiResponse<PagedResult<OrderResponse>>> GetOrdersAsync(
            int pageNumber,
            int pageSize,
            string? status = null,
            Guid? routeId = null,
            Guid? scheduleId = null)
        {
            var query = BuildOrderQuery();
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(o => o.Status == status);
            }
            if (routeId.HasValue)
            {
                query = query.Where(o => o.Schedule != null && o.Schedule.RouteId == routeId.Value);
            }
            if (scheduleId.HasValue)
            {
                query = query.Where(o => o.ScheduleId == scheduleId.Value);
            }
            query = query.OrderByDescending(o => o.CreatedAt);
            var totalRecords = await query.CountAsync();
            var pageOrders = await query
                .Skip(NormalizeSkip(pageNumber, pageSize))
                .Take(NormalizePageSize(pageSize))
                .ToListAsync();
            var contactsByEmail = await LoadCustomerContactsAsync(pageOrders);
            var orders = pageOrders
                .Select(order =>
                {
                    var contact = FindCustomerContact(contactsByEmail, order.Customer?.Email);
                    return ToOrderResponse(order, contact?.FullName, contact?.Phone);
                })
                .ToList();

            return ApiResponse<PagedResult<OrderResponse>>.SuccessResponse(
                PagedResult<OrderResponse>.Create(orders, totalRecords, pageNumber, NormalizePageSize(pageSize)),
                "Đã tải danh sách đơn hàng.");
        }

        public async Task<ApiResponse<PagedResult<OrderScheduleSummaryResponse>>> GetOrderScheduleSummaryAsync(
            DateOnly? fromDate,
            DateOnly? toDate,
            Guid? routeId,
            int pageNumber,
            int pageSize)
        {
            var vietnamToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            var effectiveFromDate = fromDate ?? vietnamToday;
            var effectiveToDate = toDate ?? effectiveFromDate.AddDays(7);

            if (effectiveToDate < effectiveFromDate)
            {
                return ApiResponse<PagedResult<OrderScheduleSummaryResponse>>.Failure(
                    "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.",
                    400);
            }

            var startDate = effectiveFromDate.ToDateTime(TimeOnly.MinValue);
            var endDate = effectiveToDate.ToDateTime(TimeOnly.MaxValue);
            var query = _db.RouteSchedules
                .AsNoTracking()
                .Where(schedule => schedule.DepartureDate >= startDate && schedule.DepartureDate <= endDate);

            if (routeId.HasValue)
            {
                query = query.Where(schedule => schedule.RouteId == routeId.Value);
            }

            query = query
                .OrderBy(schedule => schedule.DepartureDate)
                .ThenBy(schedule => schedule.DepartureTime);

            var totalRecords = await query.CountAsync();
            var safePageSize = NormalizePageSize(pageSize);
            var summaries = await query
                .Skip(NormalizeSkip(pageNumber, pageSize))
                .Take(safePageSize)
                .Select(schedule => new OrderScheduleSummaryResponse
                {
                    ScheduleId = schedule.ScheduleId,
                    ScheduleName = schedule.ScheduleName,
                    RouteId = schedule.RouteId,
                    RouteCode = schedule.Route.RouteCode,
                    OriginCity = schedule.Route.OriginCity,
                    DestCity = schedule.Route.DestCity,
                    DepartureDate = schedule.DepartureDate,
                    DepartureTime = schedule.DepartureTime,
                    CutOffTime = schedule.CutOffTime,
                    TotalOrders = _db.TransportOrders.Count(order => order.ScheduleId == schedule.ScheduleId),
                    PendingReviewCount = _db.TransportOrders.Count(order =>
                        order.ScheduleId == schedule.ScheduleId && order.Status == "PENDING_REVIEW"),
                    WaitingQuotationCount = _db.TransportOrders.Count(order =>
                        order.ScheduleId == schedule.ScheduleId
                        && (order.Status == "APPROVED" || order.Status == "QUOTING")),
                    WaitingContractCount = _db.TransportOrders.Count(order =>
                        order.ScheduleId == schedule.ScheduleId && order.Status == "CONTRACT_PENDING")
                })
                .ToListAsync();

            return ApiResponse<PagedResult<OrderScheduleSummaryResponse>>.SuccessResponse(
                PagedResult<OrderScheduleSummaryResponse>.Create(
                    summaries,
                    totalRecords,
                    pageNumber <= 0 ? 1 : pageNumber,
                    safePageSize),
                "Đã tải danh sách lịch vận chuyển và tình trạng đơn hàng.");
        }

        public async Task<ApiResponse<OrderResponse>> GetOrderByIdAsync(Guid orderId)
        {
            var order = await BuildOrderQuery()
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return ApiResponse<OrderResponse>.Failure("Order not found");

            var contactsByEmail = await LoadCustomerContactsAsync([order]);
            var contact = FindCustomerContact(contactsByEmail, order.Customer?.Email);

            return ApiResponse<OrderResponse>.SuccessResponse(
                ToOrderResponse(order, contact?.FullName, contact?.Phone),
                "Order retrieved successfully");
        }

        public async Task<ApiResponse<PagedResult<CustomerOrderSummaryResponse>>> GetOrdersByCustomerAsync(Guid customerId, int pageNumber, int pageSize, string? status = null)
        {
            var customerExists = await _db.Customers.AnyAsync(c => c.CustomerId == customerId);
            if (!customerExists)
                return ApiResponse<PagedResult<CustomerOrderSummaryResponse>>.Failure("Customer not found");

            var query = BuildOrderQuery().Where(o => o.CustomerId == customerId);
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(o => o.Status == status);
            }
            query = query.OrderByDescending(o => o.CreatedAt);
            var totalRecords = await query.CountAsync();
            var orders = await query
                .Skip(NormalizeSkip(pageNumber, pageSize))
                .Take(NormalizePageSize(pageSize))
                .Select(o => new CustomerOrderSummaryResponse
                {
                    OrderId = o.OrderId,
                    TrackingCode = o.TrackingCode,
                    ItemName = o.ItemName,
                    Category = o.Category,
                    Quantity = o.Quantity,
                    PackingType = o.PackingType,
                    TempCondition = o.TempCondition,
                    ExpectedWeightKg = o.OrderDimension != null ? o.OrderDimension.ExpectedWeightKg : 0,
                    ExpectedCbm = o.OrderDimension != null ? o.OrderDimension.ExpectedCbm : 0,
                    ReceiverName = o.ReceiverName,
                    ReceiverPhone = o.ReceiverPhone,
                    Status = o.Status,
                    MasterTripId = o.MasterTripId,
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync();

            return ApiResponse<PagedResult<CustomerOrderSummaryResponse>>.SuccessResponse(
                PagedResult<CustomerOrderSummaryResponse>.Create(orders, totalRecords, pageNumber, NormalizePageSize(pageSize)),
                "Customer orders retrieved successfully");
        }

        public async Task<ApiResponse<CreateOrderResponse>> CreateOrderAsync(CreateOrderRequest request, Guid customerId)
        {
            if (request.ExpectedWeightKg <= 0)
                return ApiResponse<CreateOrderResponse>.Failure("Expected weight must be greater than 0", 400);
            if (request.Quantity <= 0)
                return ApiResponse<CreateOrderResponse>.Failure("Quantity must be greater than 0", 400);
            if (request.LengthCm <= 0 || request.WidthCm <= 0 || request.HeightCm <= 0)
                return ApiResponse<CreateOrderResponse>.Failure("Dimensions must be greater than 0", 400);
            if (string.IsNullOrWhiteSpace(request.ItemName) || string.IsNullOrWhiteSpace(request.Category))
                return ApiResponse<CreateOrderResponse>.Failure("Item name and category are required", 400);
            if (!request.HasStrongOdor.HasValue || !request.IsStackable.HasValue)
                return ApiResponse<CreateOrderResponse>.Failure("Has_Strong_Odor and Is_Stackable are required", 400);
            if (request.LegalDocuments == null || request.LegalDocuments.Count == 0)
                return ApiResponse<CreateOrderResponse>.Failure("At least one legal document is required", 400);
            if (request.CargoPhotos == null || request.CargoPhotos.Count == 0)
                return ApiResponse<CreateOrderResponse>.Failure("At least one cargo photo is required", 400);
            var recipientValidationError = ValidateRecipient(request.ReceiverName, request.ReceiverPhone);
            if (recipientValidationError != null)
                return ApiResponse<CreateOrderResponse>.Failure(recipientValidationError, 400);

            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                var customerExists = await _db.Customers.AnyAsync(c => c.CustomerId == customerId);
                if (!customerExists)
                    return ApiResponse<CreateOrderResponse>.Failure("Customer not found");

                var schedule = await _db.RouteSchedules
                    .AsNoTracking()
                    .Include(s => s.Route)
                    .FirstOrDefaultAsync(s => s.ScheduleId == request.ScheduleId);
                    
                if (schedule == null || !string.Equals(schedule.Route?.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    return ApiResponse<CreateOrderResponse>.Failure("Schedule_ID or Route is invalid or inactive");

                var vietnamNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
                var bookingCutOff = schedule.DepartureDate.Date.Add(schedule.CutOffTime);
                if (!string.Equals(schedule.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
                    || vietnamNow >= bookingCutOff)
                {
                    return ApiResponse<CreateOrderResponse>.Failure("Schedule is no longer accepting new orders");
                }
                    
                var route = schedule.Route!;

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var expectedCbm = Math.Round(request.LengthCm * request.WidthCm * request.HeightCm * request.Quantity / 1000000m, 4);
                var coordinates = await _locationService.GetCoordinatesAsync(request.DestAddressText);

                var location = new Location
                {
                    LocationId = Guid.NewGuid(),
                    CustomerId = customerId,
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
                    TrackingCode = GenerateRequestCode(),
                    CustomerId = customerId,
                    ItemName = request.ItemName.Trim(),
                    Category = request.Category.Trim(),
                    Quantity = request.Quantity,
                    PackingType = request.PackagingType.Trim(),
                    TempCondition = request.TempCondition.ToString("0.##", CultureInfo.InvariantCulture),
                    HasStrongOdor = request.HasStrongOdor.Value,
                    IsStackable = request.IsStackable.Value,
                    ReceiverName = request.ReceiverName.Trim(),
                    ReceiverPhone = request.ReceiverPhone.Trim(),
                    OrderDimension = new OrderDimension
                    {
                        ExpectedWeightKg = request.ExpectedWeightKg,
                        ActualWeightKg = request.ExpectedWeightKg,
                        ExpectedCbm = expectedCbm,
                        ActualCbm = expectedCbm,
                        LengthCm = request.LengthCm,
                        WidthCm = request.WidthCm,
                        HeightCm = request.HeightCm
                    },
                    ScheduleId = request.ScheduleId,
                    DropoffStopId = request.DropoffStopId,
                    DestLocation = location.LocationId,
                    Status = PendingReview,
                    CreatedAt = DbNow()
                };
                _db.TransportOrders.Add(order);

                                var uploadedBy = await ResolveCustomerUserIdAsync(customerId);
                if (!uploadedBy.HasValue)
                    return ApiResponse<CreateOrderResponse>.Failure("Customer user was not found for document upload");

                if (request.LegalDocuments != null)
                {
                    foreach (var file in request.LegalDocuments)
                    {
                        if (file.Length > 10 * 1024 * 1024) return ApiResponse<CreateOrderResponse>.Failure("Legal document must be smaller than 10MB");
                        var url = await _fileService.UploadFileAsync(file);
                        _db.TransportDocuments.Add(new TransportDocument
                        {
                            DocId = Guid.NewGuid(),
                            OrderId = order.OrderId,
                            DocType = "LEGAL_DOCUMENT",
                            ImageUrl = url,
                            UploadedBy = uploadedBy.Value,
                            CreatedAt = DbNow()
                        });
                    }
                }

                if (request.CargoPhotos != null)
                {
                    foreach (var file in request.CargoPhotos)
                    {
                        if (file.Length > 10 * 1024 * 1024) return ApiResponse<CreateOrderResponse>.Failure("Cargo photo must be smaller than 10MB");
                        var url = await _fileService.UploadFileAsync(file);
                        _db.TransportDocuments.Add(new TransportDocument
                        {
                            DocId = Guid.NewGuid(),
                            OrderId = order.OrderId,
                            DocType = "ITEM_IMAGE",
                            ImageUrl = url,
                            UploadedBy = uploadedBy.Value,
                            CreatedAt = DbNow()
                        });
                    }
                }

                var draftQuotation = await BuildAutoDraftQuotationAsync(order, route, location);
                _db.Quotations.Add(draftQuotation);

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
                    order.Status,
                    DraftQuoteId = draftQuotation.QuoteId
                });

                return ApiResponse<CreateOrderResponse>.SuccessResponse(new CreateOrderResponse
                {
                    OrderId = order.OrderId,
                    TrackingCode = order.TrackingCode,
                    ItemName = order.ItemName,
                    Category = order.Category,
                    Quantity = order.Quantity,
                    PackingType = order.PackingType,
                    TempCondition = order.TempCondition,
                    ExpectedWeightKg = order.OrderDimension?.ExpectedWeightKg ?? 0,
                    ExpectedCbm = order.OrderDimension?.ExpectedCbm ?? 0,
                    ReceiverName = order.ReceiverName!,
                    ReceiverPhone = order.ReceiverPhone!,
                    Status = order.Status,
                    CreatedAt = order.CreatedAt ?? DateTime.UtcNow
                }, "Order created successfully");
            });
        }

        public async Task<ApiResponse<CreateOrderResponse>> AdminUpdateOrderAsync(Guid orderId, UpdateOrderRequest request, Guid salesUserId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var order = await _db.TransportOrders
                    .Include(o => o.OrderDimension)
                    .Include(o => o.DestLocationNavigation)
                    .Include(o => o.Schedule)
                    .ThenInclude(s => s!.Route)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order == null)
                    return ApiResponse<CreateOrderResponse>.Failure("Order not found", 404);

                if (request.ExpectedWeightKg.HasValue && request.ExpectedWeightKg.Value <= 0)
                    return ApiResponse<CreateOrderResponse>.Failure("Expected weight must be greater than 0", 400);

                if (request.Quantity.HasValue && request.Quantity.Value <= 0)
                    return ApiResponse<CreateOrderResponse>.Failure("Quantity must be greater than 0", 400);

                if (request.LengthCm.HasValue && request.LengthCm.Value <= 0)
                    return ApiResponse<CreateOrderResponse>.Failure("Length must be greater than 0", 400);

                if (request.WidthCm.HasValue && request.WidthCm.Value <= 0)
                    return ApiResponse<CreateOrderResponse>.Failure("Width must be greater than 0", 400);

                if (request.HeightCm.HasValue && request.HeightCm.Value <= 0)
                    return ApiResponse<CreateOrderResponse>.Failure("Height must be greater than 0", 400);
                if (request.ReceiverName != null || request.ReceiverPhone != null)
                {
                    var recipientValidationError = ValidateRecipient(
                        request.ReceiverName ?? order.ReceiverName,
                        request.ReceiverPhone ?? order.ReceiverPhone);
                    if (recipientValidationError != null)
                        return ApiResponse<CreateOrderResponse>.Failure(recipientValidationError, 400);
                }

                await using var transaction = await _db.Database.BeginTransactionAsync();

                if (request.ItemName != null) order.ItemName = request.ItemName.Trim();
                if (request.Category != null) order.Category = request.Category.Trim();
                if (request.Quantity.HasValue) order.Quantity = request.Quantity.Value;
                if (request.PackagingType != null) order.PackingType = request.PackagingType.Trim();
                if (request.TempCondition.HasValue) order.TempCondition = request.TempCondition.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                if (request.HasStrongOdor.HasValue) order.HasStrongOdor = request.HasStrongOdor.Value;
                if (request.IsStackable.HasValue) order.IsStackable = request.IsStackable.Value;
                if (request.ReceiverName != null) order.ReceiverName = request.ReceiverName.Trim();
                if (request.ReceiverPhone != null) order.ReceiverPhone = request.ReceiverPhone.Trim();
                
                bool dimensionChanged = false;

                if (request.ExpectedWeightKg.HasValue && order.OrderDimension != null)
                {
                    if (order.OrderDimension.ExpectedWeightKg != request.ExpectedWeightKg.Value) dimensionChanged = true;
                    order.OrderDimension.ExpectedWeightKg = request.ExpectedWeightKg.Value;
                    order.OrderDimension.ActualWeightKg = request.ExpectedWeightKg.Value;
                }
                
                if (request.LengthCm.HasValue && request.WidthCm.HasValue && request.HeightCm.HasValue && request.Quantity.HasValue && order.OrderDimension != null)
                {
                    var expectedCbm = Math.Round(request.LengthCm.Value * request.WidthCm.Value * request.HeightCm.Value * request.Quantity.Value / 1000000m, 4);
                    if (order.OrderDimension.ExpectedCbm != expectedCbm) dimensionChanged = true;
                    order.OrderDimension.ExpectedCbm = expectedCbm;
                    order.OrderDimension.ActualCbm = expectedCbm;
                    order.OrderDimension.LengthCm = request.LengthCm.Value;
                    order.OrderDimension.WidthCm = request.WidthCm.Value;
                    order.OrderDimension.HeightCm = request.HeightCm.Value;
                }

                if (request.DestAddressText != null && order.DestLocationNavigation != null)
                {
                    var coordinates = await _locationService.GetCoordinatesAsync(request.DestAddressText);
                    order.DestLocationNavigation.Address = request.DestAddressText.Trim();
                    order.DestLocationNavigation.Latitude = coordinates.Latitude;
                    order.DestLocationNavigation.Longitude = coordinates.Longitude;
                    dimensionChanged = true; // Destination change affects pricing
                }

                if (request.ScheduleId.HasValue) 
                {
                    if (order.ScheduleId != request.ScheduleId.Value) dimensionChanged = true;
                    order.ScheduleId = request.ScheduleId.Value;
                }
                if (request.DropoffStopId.HasValue) order.DropoffStopId = request.DropoffStopId.Value;

                if (request.LegalDocuments != null)
                {
                    foreach (var file in request.LegalDocuments)
                    {
                        if (file.Length > 10 * 1024 * 1024) return ApiResponse<CreateOrderResponse>.Failure("Legal document must be smaller than 10MB");
                        var url = await _fileService.UploadFileAsync(file);
                        _db.TransportDocuments.Add(new TransportDocument
                        {
                            DocId = Guid.NewGuid(),
                            OrderId = order.OrderId,
                            DocType = "LEGAL_DOCUMENT",
                            ImageUrl = url,
                            UploadedBy = salesUserId,
                            CreatedAt = DbNow()
                        });
                    }
                }

                if (request.CargoPhotos != null)
                {
                    foreach (var file in request.CargoPhotos)
                    {
                        if (file.Length > 10 * 1024 * 1024) return ApiResponse<CreateOrderResponse>.Failure("Cargo photo must be smaller than 10MB");
                        var url = await _fileService.UploadFileAsync(file);
                        _db.TransportDocuments.Add(new TransportDocument
                        {
                            DocId = Guid.NewGuid(),
                            OrderId = order.OrderId,
                            DocType = "ITEM_IMAGE",
                            ImageUrl = url,
                            UploadedBy = salesUserId,
                            CreatedAt = DbNow()
                        });
                    }
                }

                if (dimensionChanged && order.Schedule?.Route != null && order.DestLocationNavigation != null)
                {
                    var existingQuotations = await _db.Quotations.Where(q => q.OrderId == orderId).ToListAsync();
                    if (existingQuotations.Any())
                    {
                        _db.Quotations.RemoveRange(existingQuotations);
                        
                        var draftQuotation = await BuildAutoDraftQuotationAsync(order, order.Schedule.Route, order.DestLocationNavigation);
                        _db.Quotations.Add(draftQuotation);
                    }
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await SendOrderUpdatedNotificationAsync(order);

                return ApiResponse<CreateOrderResponse>.SuccessResponse(new CreateOrderResponse
                {
                    OrderId = order.OrderId,
                    TrackingCode = order.TrackingCode,
                    ItemName = order.ItemName,
                    Category = order.Category,
                    Quantity = order.Quantity,
                    PackingType = order.PackingType,
                    TempCondition = order.TempCondition,
                    ExpectedWeightKg = order.OrderDimension?.ExpectedWeightKg ?? 0,
                    ExpectedCbm = order.OrderDimension?.ExpectedCbm ?? 0,
                    ReceiverName = order.ReceiverName ?? string.Empty,
                    ReceiverPhone = order.ReceiverPhone ?? string.Empty,
                    Status = order.Status,
                    CreatedAt = order.CreatedAt ?? DateTime.UtcNow
                }, "Order updated successfully by Admin");
            });
        }

        public async Task<ApiResponse<CreateOrderResponse>> UpdateOrderAsync(Guid orderId, UpdateOrderRequest request, Guid customerId)
        {
            if (request.ExpectedWeightKg.HasValue && request.ExpectedWeightKg <= 0)
                return ApiResponse<CreateOrderResponse>.Failure("Expected weight must be greater than 0", 400);
            if (request.Quantity.HasValue && request.Quantity <= 0)
                return ApiResponse<CreateOrderResponse>.Failure("Quantity must be greater than 0", 400);
            if ((request.LengthCm.HasValue && request.LengthCm <= 0) || (request.WidthCm.HasValue && request.WidthCm <= 0) || (request.HeightCm.HasValue && request.HeightCm <= 0))
                return ApiResponse<CreateOrderResponse>.Failure("Dimensions must be greater than 0", 400);

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var order = await _db.TransportOrders
                    .Include(o => o.OrderDimension)
                    .Include(o => o.DestLocationNavigation)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

                if (order == null)
                    return ApiResponse<CreateOrderResponse>.Failure("Order not found or you don't have permission");

                if (request.ReceiverName != null || request.ReceiverPhone != null)
                {
                    var recipientValidationError = ValidateRecipient(
                        request.ReceiverName ?? order.ReceiverName,
                        request.ReceiverPhone ?? order.ReceiverPhone);
                    if (recipientValidationError != null)
                        return ApiResponse<CreateOrderResponse>.Failure(recipientValidationError, 400);
                }

                if (!string.Equals(order.Status, PendingReview, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(order.Status, "NEEDS_UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponse<CreateOrderResponse>.Failure(
                        "Order can only be updated while pending review or when it requires an update (PENDING_REVIEW, NEEDS_UPDATE)");
                }

                await using var transaction = await _db.Database.BeginTransactionAsync();

                if (request.ItemName != null) order.ItemName = request.ItemName.Trim();
                if (request.Category != null) order.Category = request.Category.Trim();
                if (request.Quantity.HasValue) order.Quantity = request.Quantity.Value;
                if (request.PackagingType != null) order.PackingType = request.PackagingType.Trim();
                if (request.TempCondition.HasValue) order.TempCondition = request.TempCondition.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                if (request.HasStrongOdor.HasValue) order.HasStrongOdor = request.HasStrongOdor.Value;
                if (request.IsStackable.HasValue) order.IsStackable = request.IsStackable.Value;
                if (request.ReceiverName != null) order.ReceiverName = request.ReceiverName.Trim();
                if (request.ReceiverPhone != null) order.ReceiverPhone = request.ReceiverPhone.Trim();
                
                if (request.ExpectedWeightKg.HasValue && order.OrderDimension != null)
                {
                    order.OrderDimension.ExpectedWeightKg = request.ExpectedWeightKg.Value;
                    order.OrderDimension.ActualWeightKg = request.ExpectedWeightKg.Value;
                }
                
                if (request.LengthCm.HasValue && request.WidthCm.HasValue && request.HeightCm.HasValue && request.Quantity.HasValue && order.OrderDimension != null)
                {
                    var expectedCbm = Math.Round(request.LengthCm.Value * request.WidthCm.Value * request.HeightCm.Value * request.Quantity.Value / 1000000m, 4);
                    order.OrderDimension.ExpectedCbm = expectedCbm;
                    order.OrderDimension.ActualCbm = expectedCbm;
                    order.OrderDimension.LengthCm = request.LengthCm.Value;
                    order.OrderDimension.WidthCm = request.WidthCm.Value;
                    order.OrderDimension.HeightCm = request.HeightCm.Value;
                }

                if (request.DestAddressText != null && order.DestLocationNavigation != null)
                {
                    var coordinates = await _locationService.GetCoordinatesAsync(request.DestAddressText);
                    order.DestLocationNavigation.Address = request.DestAddressText.Trim();
                    order.DestLocationNavigation.Latitude = coordinates.Latitude;
                    order.DestLocationNavigation.Longitude = coordinates.Longitude;
                }

                if (request.ScheduleId.HasValue) order.ScheduleId = request.ScheduleId.Value;
                if (request.DropoffStopId.HasValue) order.DropoffStopId = request.DropoffStopId.Value;

                var uploadedBy = await ResolveCustomerUserIdAsync(customerId);
                if (request.LegalDocuments != null && uploadedBy.HasValue)
                {
                    foreach (var file in request.LegalDocuments)
                    {
                        if (file.Length > 10 * 1024 * 1024) return ApiResponse<CreateOrderResponse>.Failure("Legal document must be smaller than 10MB");
                        var url = await _fileService.UploadFileAsync(file);
                        _db.TransportDocuments.Add(new TransportDocument
                        {
                            DocId = Guid.NewGuid(),
                            OrderId = order.OrderId,
                            DocType = "LEGAL_DOCUMENT",
                            ImageUrl = url,
                            UploadedBy = uploadedBy.Value,
                            CreatedAt = DbNow()
                        });
                    }
                }

                if (request.CargoPhotos != null && uploadedBy.HasValue)
                {
                    foreach (var file in request.CargoPhotos)
                    {
                        if (file.Length > 10 * 1024 * 1024) return ApiResponse<CreateOrderResponse>.Failure("Cargo photo must be smaller than 10MB");
                        var url = await _fileService.UploadFileAsync(file);
                        _db.TransportDocuments.Add(new TransportDocument
                        {
                            DocId = Guid.NewGuid(),
                            OrderId = order.OrderId,
                            DocType = "ITEM_IMAGE",
                            ImageUrl = url,
                            UploadedBy = uploadedBy.Value,
                            CreatedAt = DbNow()
                        });
                    }
                }

                if (string.Equals(order.Status, "NEEDS_UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    order.Status = PendingReview;
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await SendOrderUpdatedNotificationAsync(order);

                return ApiResponse<CreateOrderResponse>.SuccessResponse(new CreateOrderResponse
                {
                    OrderId = order.OrderId,
                    TrackingCode = order.TrackingCode,
                    ItemName = order.ItemName,
                    Category = order.Category,
                    Quantity = order.Quantity,
                    PackingType = order.PackingType,
                    TempCondition = order.TempCondition,
                    ExpectedWeightKg = order.OrderDimension?.ExpectedWeightKg ?? 0,
                    ExpectedCbm = order.OrderDimension?.ExpectedCbm ?? 0,
                    ReceiverName = order.ReceiverName ?? string.Empty,
                    ReceiverPhone = order.ReceiverPhone ?? string.Empty,
                    Status = order.Status,
                    CreatedAt = order.CreatedAt ?? DateTime.UtcNow
                }, "Order updated successfully");
            });
        }

        public async Task<ApiResponse<bool>> UploadPhysicalPodAsync(Guid orderId, string physicalPodImageUrl)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var order = await _db.TransportOrders
                        .Include(o => o.DeliveryEpods)
                        .FirstOrDefaultAsync(o => o.OrderId == orderId);

                    if (order == null)
                        return ApiResponse<bool>.Failure("Order not found");
                    
                    var epod = order.DeliveryEpods.FirstOrDefault();
                    if (epod == null)
                        return ApiResponse<bool>.Failure("Epod not found for order");

                    var firstUserId = (await _db.Users.FirstOrDefaultAsync())?.UserId ?? Guid.Empty;
                    _db.TransportDocuments.Add(new ColdChainX.Core.Entities.TransportDocument
                    {
                        DocId = Guid.NewGuid(),
                        OrderId = orderId,
                        DocType = "PHYSICAL_POD",
                        ImageUrl = physicalPodImageUrl,
                        UploadedBy = firstUserId,
                        CreatedAt = DateTime.UtcNow
                    });
                    
                    var lpns = await _db.Lpns.Where(l => l.OrderId == orderId).ToListAsync();
                    foreach (var lpn in lpns)
                    {
                        if (lpn.State != ColdChainX.Core.Enums.LpnState.DELIVERY_RETURNED &&
                            lpn.State != ColdChainX.Core.Enums.LpnState.RETURN_PENDING)
                        {
                            lpn.State = ColdChainX.Core.Enums.LpnState.DELIVERED;
                        }
                    }

                    order.Status = "COMPLETED";

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return ApiResponse<bool>.SuccessResponse(true, "Physical POD uploaded successfully. Inventory deducted and Invoice generation triggered (Mock).");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return ApiResponse<bool>.Failure($"Upload POD failed: {ex.Message}");
                }
            });
        }

        public async Task<ApiResponse<ReviewOrderResponse>> ReviewOrderAsync(Guid orderId, ReviewOrderRequest request, Guid salesUserId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                var order = await _db.TransportOrders
                .Include(o => o.Customer)
                .Include(o => o.Schedule).ThenInclude(s => s.Route)
                .Include(o => o.DestLocationNavigation)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order == null)
                    return ApiResponse<ReviewOrderResponse>.Failure("Order not found");

                if (!string.Equals(order.Status, PendingReview, StringComparison.OrdinalIgnoreCase))
                    return ApiResponse<ReviewOrderResponse>.Failure("Order is not pending review");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var action = request.Action.Trim().ToUpperInvariant();
                                if (action == "REQUEST_UPDATE")
                {
                    if (string.IsNullOrWhiteSpace(request.CustomerNote))
                        return ApiResponse<ReviewOrderResponse>.Failure("CustomerNote is required when action is REQUEST_UPDATE");

                    order.Status = "NEEDS_UPDATE";

                    var customerUserId = await ResolveCustomerUserIdAsync(order.CustomerId);
                    if (_notificationService == null)
                    {
                        await AddNotificationAsync(
                            customerUserId,
                            salesUserId,
                            "NOTI_ORDER_NEEDS_UPDATE",
                            order.OrderId,
                            new { Tracking_Code = order.TrackingCode, Request_Reason = request.CustomerNote });
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await SendOrderUpdatedNotificationAsync(order, customerUserId);

                    await _hubContext.Clients.User(order.CustomerId.ToString()!).SendAsync("OrderNeedsUpdate", new
                    {
                        order.OrderId,
                        order.TrackingCode,
                        RejectReason = request.CustomerNote
                    });

                    return ApiResponse<ReviewOrderResponse>.SuccessResponse(new ReviewOrderResponse
                    {
                        OrderId = order.OrderId,
                        TrackingCode = order.TrackingCode,
                        Status = order.Status
                    }, "Order requires document update");
                }
                
                if (action == "COMPLIANCE_REJECT")
                {
                    if (string.IsNullOrWhiteSpace(request.CustomerNote))
                        return ApiResponse<ReviewOrderResponse>.Failure("CustomerNote is required when action is COMPLIANCE_REJECT");

                    order.Status = Rejected;

                    var customerUserId = await ResolveCustomerUserIdAsync(order.CustomerId);
                    if (_notificationService == null)
                    {
                        await AddNotificationAsync(
                            customerUserId,
                            salesUserId,
                            "NOTI_ORDER_REJECTED",
                            order.OrderId,
                            new { Tracking_Code = order.TrackingCode, Reject_Reason = request.CustomerNote });
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await SendOrderUpdatedNotificationAsync(order, customerUserId);

                    await _hubContext.Clients.User(order.CustomerId.ToString()!).SendAsync("OrderRejected", new
                    {
                        order.OrderId,
                        order.TrackingCode,
                        RejectReason = request.CustomerNote
                    });

                    return ApiResponse<ReviewOrderResponse>.SuccessResponse(new ReviewOrderResponse
                    {
                        OrderId = order.OrderId,
                        TrackingCode = order.TrackingCode,
                        Status = order.Status
                    }, "Order rejected due to legal compliance violation");
                }

                if (action != "APPROVE")
                    return ApiResponse<ReviewOrderResponse>.Failure("Action must be APPROVE, REQUEST_UPDATE, or COMPLIANCE_REJECT");

                var quotation = await _db.Quotations
                    .Where(q => q.OrderId == order.OrderId && q.Status == Draft)
                    .OrderByDescending(q => q.CreatedAt)
                    .FirstOrDefaultAsync();

                if (quotation == null)
                    return ApiResponse<ReviewOrderResponse>.Failure("Draft quotation was not found for this order");

                order.Status = Approved;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await SendOrderUpdatedNotificationAsync(order);

                return ApiResponse<ReviewOrderResponse>.SuccessResponse(new ReviewOrderResponse
                {
                    OrderId = order.OrderId,
                    TrackingCode = order.TrackingCode,
                    Status = order.Status,
                    QuoteId = quotation.QuoteId,
                    BaseFreight = quotation.BaseFreight,
                    LastMileSurcharge = quotation.LastMileSurcharge,
                    VatAmount = quotation.VatAmount,
                    FinalAmount = quotation.FinalAmount
                }, "Order approved for quotation review");
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
                                || u.Role.RoleName.ToLower() == "warehouseworker"))
                .Select(u => (Guid?)u.UserId)
                .FirstOrDefaultAsync();
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
            var tier = await _db.WeightTiers
                .AsNoTracking()
                .Include(t => t.Route)
                .Where(t => t.RouteId == order.Schedule.RouteId
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
                ["Actual_CBM"] = (order.OrderDimension?.ActualCbm ?? 0m).ToString("0.####", CultureInfo.InvariantCulture),
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

        private async Task SendOrderUpdatedNotificationAsync(
            TransportOrder order,
            Guid? resolvedCustomerUserId = null)
        {
            if (_notificationService == null)
                return;

            try
            {
                var userId = resolvedCustomerUserId ?? await ResolveCustomerUserIdAsync(order.CustomerId);
                if (!userId.HasValue)
                    return;

                await _notificationService.SendToUserAsync(
                    userId.Value,
                    "Đơn hàng đã được cập nhật",
                    "Trạng thái đơn hàng của bạn vừa thay đổi.",
                    "ORDER_UPDATED",
                    order.OrderId.ToString(),
                    new Dictionary<string, string>
                    {
                        ["orderId"] = order.OrderId.ToString(),
                        ["status"] = order.Status,
                        ["screen"] = "order-detail"
                    });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Firebase order update notification failed after the order transaction committed. OrderId: {OrderId}.",
                    order.OrderId);
            }
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

        private static string GenerateRequestCode()
            => $"REQ-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

        private static int NormalizePageSize(int pageSize)
            => Math.Clamp(pageSize <= 0 ? 10 : pageSize, 1, 100);

        private static int NormalizeSkip(int pageNumber, int pageSize)
        {
            var safePageNumber = pageNumber <= 0 ? 1 : pageNumber;
            return (safePageNumber - 1) * NormalizePageSize(pageSize);
        }

        private static string? ValidateRecipient(string? receiverName, string? receiverPhone)
        {
            if (string.IsNullOrWhiteSpace(receiverName))
                return "Receiver name is required";
            if (receiverName.Trim().Length > 100)
                return "Receiver name must not exceed 100 characters";
            if (string.IsNullOrWhiteSpace(receiverPhone))
                return "Receiver phone is required";
            if (receiverPhone.Trim().Length > 20)
                return "Receiver phone must not exceed 20 characters";

            var digitCount = receiverPhone.Count(char.IsDigit);
            if (digitCount is < 8 or > 15
                || receiverPhone.Any(character => !char.IsDigit(character)
                    && character is not ('+' or ' ' or '-' or '(' or ')')))
            {
                return "Receiver phone must contain between 8 and 15 digits";
            }

            return null;
        }

        private IQueryable<TransportOrder> BuildOrderQuery()
        {
            return _db.TransportOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Schedule).ThenInclude(s => s.Route)
                .Include(o => o.DestLocationNavigation)
                .Include(o => o.OrderDimension)
                .Include(o => o.TransportDocuments)
                .Include(o => o.Quotations);
        }

        private async Task<IReadOnlyDictionary<string, CustomerContact>> LoadCustomerContactsAsync(
            IEnumerable<TransportOrder> orders)
        {
            var customerEmails = orders
                .Select(order => order.Customer?.Email)
                .Where(email => !string.IsNullOrWhiteSpace(email))
                .Select(email => email!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (customerEmails.Count == 0)
                return new Dictionary<string, CustomerContact>(StringComparer.OrdinalIgnoreCase);

            var users = await _db.Users
                .AsNoTracking()
                .Where(user => user.Email != null && customerEmails.Contains(user.Email))
                .Select(user => new
                {
                    Email = user.Email!,
                    user.FullName,
                    user.Phone
                })
                .ToListAsync();

            return users
                .GroupBy(user => user.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => new CustomerContact(group.First().FullName, group.First().Phone),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static CustomerContact? FindCustomerContact(
            IReadOnlyDictionary<string, CustomerContact> contactsByEmail,
            string? customerEmail)
        {
            return !string.IsNullOrWhiteSpace(customerEmail)
                && contactsByEmail.TryGetValue(customerEmail, out var contact)
                    ? contact
                    : null;
        }

        private static OrderResponse ToOrderResponse(
            TransportOrder order,
            string? customerContactName = null,
            string? customerPhone = null)
        {

            return new OrderResponse
            {
                OrderId = order.OrderId,
                TrackingCode = order.TrackingCode,
                ItemName = order.ItemName,
                Category = order.Category,
                Quantity = order.Quantity,
                PackingType = order.PackingType,
                TempCondition = order.TempCondition,
                ExpectedWeightKg = (order.OrderDimension?.ExpectedWeightKg ?? 0m),
                ActualWeightKg = (order.OrderDimension?.ActualWeightKg ?? 0m),
                ExpectedCbm = (order.OrderDimension?.ExpectedCbm ?? 0m),
                ActualCbm = (order.OrderDimension?.ActualCbm ?? 0m),
                LengthCm = order.OrderDimension?.LengthCm,
                WidthCm = order.OrderDimension?.WidthCm,
                HeightCm = order.OrderDimension?.HeightCm,
                DropoffStopId = order.DropoffStopId,
                Status = order.Status,
                MasterTripId = order.MasterTripId,
                CreatedAt = order.CreatedAt,
                Route = order.Schedule?.Route == null
                    ? null
                    : new OrderRouteResponse
                    {
                        RouteId = order.Schedule.RouteId,
                        RouteCode = order.Schedule.Route.RouteCode,
                        OriginCity = order.Schedule.Route.OriginCity,
                        DestCity = order.Schedule.Route.DestCity,
                        TransitTime = order.Schedule.Route.TransitTime,
                        CutOffTime = order.Schedule.Route.CutOffTime
                    },
                Schedule = order.Schedule == null
                    ? null
                    : new OrderScheduleResponse
                    {
                        ScheduleId = order.Schedule.ScheduleId,
                        ScheduleName = order.Schedule.ScheduleName,
                        DepartureDate = order.Schedule.DepartureDate,
                        DepartureTime = order.Schedule.DepartureTime,
                        CutOffTime = order.Schedule.CutOffTime,
                        Status = order.Schedule.Status
                    },
                Destination = order.DestLocationNavigation == null
                    ? null
                    : new OrderLocationResponse
                    {
                        LocationId = order.DestLocationNavigation.LocationId,
                        Address = order.DestLocationNavigation.Address
                    },
                Documents = order.TransportDocuments
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => new OrderDocumentResponse
                    {
                        DocId = d.DocId,
                        DocType = d.DocType,
                        ImageUrl = d.ImageUrl,
                        CreatedAt = d.CreatedAt
                    })
                    .ToList(),
                CustomerId = order.CustomerId,
                CustomerName = order.Customer?.CompanyName,
                CustomerContactName = customerContactName,
                CustomerPhone = customerPhone,
                Quotations = order.Quotations
                    .OrderByDescending(q => q.CreatedAt)
                    .Select(q => new OrderQuotationResponse
                    {
                        QuoteId = q.QuoteId,
                        BaseFreight = q.BaseFreight,
                        LastMileSurcharge = q.LastMileSurcharge,
                        VatPercentage = q.VatPercentage,
                        VatAmount = q.VatAmount,
                        FinalAmount = q.FinalAmount,
                        FileUrl = q.FileUrl,
                        Status = q.Status,
                        CreatedAt = q.CreatedAt
                    })
                    .ToList()
            };
        }

        private sealed record CustomerContact(string FullName, string? Phone);

        private sealed record RoutePricing(
            decimal BaseFreight,
            decimal PriceKg,
            decimal FreightByKg,
            decimal ChargeableWeightKg,
            string OriginCity,
            string DestinationCity);

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

        public async Task<ApiResponse<IReadOnlyCollection<ColdChainX.Application.DTOs.Routes.WarehouseOptionDto>>> GetOriginWarehousesForOrderAsync(Guid orderId)
        {
            var order = await _db.TransportOrders
                .Include(o => o.Schedule)
                .ThenInclude(s => s.Route)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null) return ApiResponse<IReadOnlyCollection<ColdChainX.Application.DTOs.Routes.WarehouseOptionDto>>.Failure("Order not found");
            if (order.Schedule?.Route == null) return ApiResponse<IReadOnlyCollection<ColdChainX.Application.DTOs.Routes.WarehouseOptionDto>>.Failure("Route information not found for this order");

            var originCity = order.Schedule.Route.OriginCity;

            var warehouses = await _db.Warehouses
                .Where(w => w.WarehouseName.Contains(originCity) || 
                            w.WarehouseCode.Contains(originCity) || 
                            (w.Address != null && w.Address.Contains(originCity)))
                .Select(w => new ColdChainX.Application.DTOs.Routes.WarehouseOptionDto
                {
                    WarehouseId = w.WarehouseId,
                    WarehouseName = w.WarehouseName,
                    Address = w.Address
                })
                .ToListAsync();

            return ApiResponse<IReadOnlyCollection<ColdChainX.Application.DTOs.Routes.WarehouseOptionDto>>.SuccessResponse(warehouses, "Available warehouses retrieved successfully");
        }

        private static string FormatKg(decimal value)
        {
            return value.ToString("#,##0.##", CultureInfo.GetCultureInfo("vi-VN"));
        }

        public async Task<ApiResponse<PublicTrackingResponseDto>> GetPublicTrackingAsync(string trackingCode)
        {
            var order = await _db.TransportOrders
                .Include(o => o.DestLocationNavigation)
                .Include(o => o.DropoffStop)
                
                .FirstOrDefaultAsync(o => o.TrackingCode == trackingCode);

            if (order == null)
            {
                return ApiResponse<PublicTrackingResponseDto>.Failure("Không tìm thấy đơn hàng với mã này.", 404);
            }

            var destLat = order.DestLocationNavigation?.Latitude ?? order.DestLocationNavigation?.Latitude;
            var destLng = order.DestLocationNavigation?.Longitude ?? order.DestLocationNavigation?.Longitude;
            var destAddress = order.DestLocationNavigation?.Address ?? order.DestLocationNavigation?.Address ?? "N/A";

            var response = new PublicTrackingResponseDto
            {
                TrackingCode = order.TrackingCode,
                Status = order.Status,
                ItemName = order.ItemName,
                DeliveryAddress = destAddress,
                LastUpdatedAt = DateTime.UtcNow
            };

            if ((order.Status == "DISPATCHED" || order.Status == "IN_TRANSIT" || order.Status == "COMPLETED") && order.MasterTripId.HasValue)
            {
                var latestTelemetry = await _db.TelemetryLogs
                    .Where(t => t.TripId == order.MasterTripId.Value)
                    .OrderByDescending(t => t.Timestamp)
                    .FirstOrDefaultAsync();

                if (latestTelemetry != null)
                {
                    response.CurrentLatitude = (double)latestTelemetry.Latitude;
                    response.CurrentLongitude = (double)latestTelemetry.Longitude;
                    response.CurrentTemperature = latestTelemetry.Temperature;
                    response.LastUpdatedAt = latestTelemetry.Timestamp;

                    if (destLat.HasValue && destLng.HasValue && order.Status != "COMPLETED")
                    {
                        var distance = CalculateDistance((double)latestTelemetry.Latitude, (double)latestTelemetry.Longitude, (double)destLat.Value, (double)destLng.Value);
                        response.RemainingDistanceKm = Math.Round(distance, 2);
                        
                        var speedKmH = 40.0;
                        response.EstimatedMinutesToArrival = (int)Math.Ceiling((distance / speedKmH) * 60);
                    }
                }
            }

            return ApiResponse<PublicTrackingResponseDto>.SuccessResponse(response);
        }

        private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371; // Radius of the earth in km
            var dLat = Deg2Rad(lat2 - lat1);
            var dLon = Deg2Rad(lon2 - lon1);
            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            var d = R * c; // Distance in km
            return d;
        }

        private static double Deg2Rad(double deg)
        {
            return deg * (Math.PI / 180);
        }

        public async Task<ApiResponse<object>> GetPublicTemperatureChartAsync(string trackingCode, int maxPoints = 200)
        {
            if (maxPoints <= 0 || maxPoints > 10000)
            {
                return ApiResponse<object>.Failure("Invalid max points parameter.", 400);
            }

            var order = await _db.TransportOrders
                .Include(o => o.MasterTrip)
                .Include(o => o.DeliveryEpods)
                .FirstOrDefaultAsync(o => o.TrackingCode == trackingCode);

            if (order == null)
            {
                return ApiResponse<object>.Failure("Không tìm thấy đơn hàng với mã này.", 404);
            }

            if (order.MasterTripId == null)
            {
                return ApiResponse<object>.Failure("Đơn hàng chưa được điều phối hoặc chưa có dữ liệu hành trình.", 400);
            }

            var startTime = order.MasterTrip?.StartedAt ?? order.MasterTrip?.PlannedStartTime ?? DateTime.UtcNow;
            var endTime = DateTime.UtcNow;

            if (order.Status == "COMPLETED")
            {
                var epod = order.DeliveryEpods.FirstOrDefault();
                if (epod != null && (epod.CreatedAt.HasValue || epod.SignedAt.HasValue))
                {
                    endTime = epod.CreatedAt ?? epod.SignedAt ?? DateTime.UtcNow;
                }
                else if (order.MasterTrip?.CompletedAt != null)
                {
                    endTime = order.MasterTrip.CompletedAt.Value;
                }
            }

            var rawLogs = await _db.TelemetryLogs
                .Where(t => t.TripId == order.MasterTripId.Value && t.Timestamp >= startTime && t.Timestamp <= endTime)
                .OrderBy(t => t.Timestamp)
                .Select(t => new
                {
                    t.Timestamp,
                    t.Temperature,
                    t.Latitude,
                    t.Longitude
                })
                .ToListAsync();

            var points = rawLogs
                .Select(t => new ColdChainX.Application.Helpers.TrackingPoint(t.Timestamp, t.Temperature, t.Latitude, t.Longitude))
                .ToList();
                
            var sampledPoints = ColdChainX.Application.Helpers.TrackingDownsampler.Downsample(points, Math.Clamp(maxPoints, 20, 1000));

            return ApiResponse<object>.SuccessResponse(new
            {
                TrackingCode = trackingCode,
                StartTime = startTime,
                EndTime = endTime,
                RawPointCount = points.Count,
                SampledPointCount = sampledPoints.Count,
                Points = sampledPoints.Select(t => new
                {
                    t.Timestamp,
                    TempC = t.TempC
                })
            });
        }

        public async Task<ApiResponse<byte[]>> ExportDigitalArchiveAsync(Guid orderId)
        {
            var order = await _db.TransportOrders
                .Include(o => o.TransportDocuments)
                .Include(o => o.DeliveryEpods)
                .Include(o => o.InvoiceLines)
                    .ThenInclude(il => il.Invoice)
                .Include(o => o.Claims)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return ApiResponse<byte[]>.Failure("Order not found");

            using var memoryStream = new System.IO.MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                if (order.DeliveryEpods != null && order.DeliveryEpods.Any())
                {
                    foreach (var epod in order.DeliveryEpods)
                    {
                        var podUrl = order.TransportDocuments?.FirstOrDefault(d => d.DocType == "PHYSICAL_POD")?.ImageUrl ?? "N/A";
                        var entry = archive.CreateEntry($"POD_{epod.EpodId}.txt");
                        using var entryStream = entry.Open();
                        using var writer = new System.IO.StreamWriter(entryStream);
                        writer.WriteLine($"Physical POD URL: {podUrl}");
                        writer.WriteLine($"Customer Signature URL: {epod.SignImageUrl}");
                    }
                }

                var invoices = order.InvoiceLines?.Select(il => il.Invoice).Where(i => i != null).Distinct().ToList();
                if (invoices != null && invoices.Any())
                {
                    foreach (var invoice in invoices)
                    {
                        var entry = archive.CreateEntry($"Invoice_{invoice.InvoiceId}.txt");
                        using var entryStream = entry.Open();
                        using var writer = new System.IO.StreamWriter(entryStream);
                        writer.WriteLine($"Invoice ID: {invoice.InvoiceId}");
                        writer.WriteLine($"Grand Total: {invoice.GrandTotal}");
                        writer.WriteLine($"Status: {invoice.Status}");
                    }
                }

                if (order.Claims != null && order.Claims.Any())
                {
                    foreach (var claim in order.Claims)
                    {
                        var entry = archive.CreateEntry($"Claim_{claim.ClaimId}.txt");
                        using var entryStream = entry.Open();
                        using var writer = new System.IO.StreamWriter(entryStream);
                        writer.WriteLine($"Claim ID: {claim.ClaimId}");
                        writer.WriteLine($"Reason: {claim.Description}");
                        writer.WriteLine($"Status: {claim.Status}");
                        var evidenceUrls = string.Join(", ", claim.ClaimEvidences?.Select(e => e.ImageUrl) ?? Array.Empty<string>());
                        writer.WriteLine($"Evidence URLs: {evidenceUrls}");
                    }
                }
            }

            return ApiResponse<byte[]>.SuccessResponse(memoryStream.ToArray(), "Archive generated successfully.");
        }
    }
}




