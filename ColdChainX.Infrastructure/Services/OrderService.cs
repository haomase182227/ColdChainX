using System.Text.Json;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
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

        public OrderService(
            ApplicationDbContext db,
            ILocationService locationService,
            IFileService fileService,
            IPdfService pdfService,
            IWebHostEnvironment environment,
            IHubContext<NotificationHub> hubContext)
        {
            _db = db;
            _locationService = locationService;
            _fileService = fileService;
            _pdfService = pdfService;
            _environment = environment;
            _hubContext = hubContext;
        }

        public async Task<ApiResponse<PagedResult<OrderResponse>>> GetOrdersAsync(int pageNumber, int pageSize, string? status = null)
        {
            var query = BuildOrderQuery();
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(o => o.Status == status);
            }
            query = query.OrderByDescending(o => o.CreatedAt);
            var totalRecords = await query.CountAsync();
            var orders = await query
                .Skip(NormalizeSkip(pageNumber, pageSize))
                .Take(NormalizePageSize(pageSize))
                .Select(o => ToOrderResponse(o))
                .ToListAsync();

            return ApiResponse<PagedResult<OrderResponse>>.SuccessResponse(
                PagedResult<OrderResponse>.Create(orders, totalRecords, pageNumber, NormalizePageSize(pageSize)),
                "Orders retrieved successfully");
        }

        public async Task<ApiResponse<OrderResponse>> GetOrderByIdAsync(Guid orderId)
        {
            var order = await BuildOrderQuery()
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return ApiResponse<OrderResponse>.Failure("Order not found");

            return ApiResponse<OrderResponse>.SuccessResponse(ToOrderResponse(order), "Order retrieved successfully");
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
                    Status = o.Status,
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync();

            return ApiResponse<PagedResult<CustomerOrderSummaryResponse>>.SuccessResponse(
                PagedResult<CustomerOrderSummaryResponse>.Create(orders, totalRecords, pageNumber, NormalizePageSize(pageSize)),
                "Customer orders retrieved successfully");
        }

        public async Task<ApiResponse<CreateOrderResponse>> CreateOrderAsync(CreateOrderRequest request, Guid customerId)
        {
            
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
                    
                if (schedule == null || schedule.Route?.Status != "ACTIVE")
                    return ApiResponse<CreateOrderResponse>.Failure("Schedule_ID or Route is invalid or inactive");
                    
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
                    HasStrongOdor = request.HasStrongOdor,
                    IsStackable = request.IsStackable,
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
                    return ApiResponse<CreateOrderResponse>.Failure("Order not found");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                if (request.ItemName != null) order.ItemName = request.ItemName.Trim();
                if (request.Category != null) order.Category = request.Category.Trim();
                if (request.Quantity.HasValue) order.Quantity = request.Quantity.Value;
                if (request.PackagingType != null) order.PackingType = request.PackagingType.Trim();
                if (request.TempCondition.HasValue) order.TempCondition = request.TempCondition.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                if (request.HasStrongOdor.HasValue) order.HasStrongOdor = request.HasStrongOdor.Value;
                if (request.IsStackable.HasValue) order.IsStackable = request.IsStackable.Value;
                
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

                // If dimension/destination changed and it's already approved/quoted, regenerate quotation
                if (dimensionChanged && order.Schedule?.Route != null && order.DestLocationNavigation != null)
                {
                    var existingQuotations = await _db.Quotations.Where(q => q.OrderId == orderId).ToListAsync();
                    if (existingQuotations.Any())
                    {
                        // Set them as Obsolete (using string constant or just deleting them, here we change status if string, or simply delete)
                        _db.Quotations.RemoveRange(existingQuotations);
                        
                        var draftQuotation = await BuildAutoDraftQuotationAsync(order, order.Schedule.Route, order.DestLocationNavigation);
                        _db.Quotations.Add(draftQuotation);
                    }
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

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
                    Status = order.Status,
                    CreatedAt = order.CreatedAt ?? DateTime.UtcNow
                }, "Order updated successfully by Admin");
            });
        }

        public async Task<ApiResponse<CreateOrderResponse>> UpdateOrderAsync(Guid orderId, UpdateOrderRequest request, Guid customerId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var order = await _db.TransportOrders
                    .Include(o => o.OrderDimension)
                    .Include(o => o.DestLocationNavigation)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

                if (order == null)
                    return ApiResponse<CreateOrderResponse>.Failure("Order not found or you don't have permission");

                if (order.Status != "NEEDS_UPDATE")
                    return ApiResponse<CreateOrderResponse>.Failure("Order can only be updated when it requires an update (NEEDS_UPDATE)");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                if (request.ItemName != null) order.ItemName = request.ItemName.Trim();
                if (request.Category != null) order.Category = request.Category.Trim();
                if (request.Quantity.HasValue) order.Quantity = request.Quantity.Value;
                if (request.PackagingType != null) order.PackingType = request.PackagingType.Trim();
                if (request.TempCondition.HasValue) order.TempCondition = request.TempCondition.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                if (request.HasStrongOdor.HasValue) order.HasStrongOdor = request.HasStrongOdor.Value;
                if (request.IsStackable.HasValue) order.IsStackable = request.IsStackable.Value;
                
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

                if (order.Status == "NEEDS_UPDATE")
                {
                    order.Status = PendingReview;
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

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
                    Status = order.Status,
                    CreatedAt = order.CreatedAt ?? DateTime.UtcNow
                }, "Order updated successfully");
            });
        }

        public async Task<ApiResponse<bool>> DeleteOrderAsync(Guid orderId, Guid customerId)
        {
            var order = await _db.TransportOrders
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);
                
            if (order == null) return ApiResponse<bool>.Failure("Order not found or permission denied");
            
            if (order.Status != PendingReview && order.Status != "NEEDS_UPDATE")
                return ApiResponse<bool>.Failure("Cannot delete order at this stage");

            _db.TransportOrders.Remove(order);
            await _db.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Order deleted successfully");
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
                    await AddNotificationAsync(
                        customerUserId,
                        salesUserId,
                        "NOTI_ORDER_NEEDS_UPDATE",
                        order.OrderId,
                        new { Tracking_Code = order.TrackingCode, Request_Reason = request.CustomerNote });

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

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
                    await AddNotificationAsync(
                        customerUserId,
                        salesUserId,
                        "NOTI_ORDER_REJECTED",
                        order.OrderId,
                        new { Tracking_Code = order.TrackingCode, Reject_Reason = request.CustomerNote });

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

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
                                || u.Role.RoleName.ToLower() == "manager"))
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

        private static OrderResponse ToOrderResponse(TransportOrder order)
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
                Status = order.Status,
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
    }
}






