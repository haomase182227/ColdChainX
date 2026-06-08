using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
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
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<NotificationHub> _hubContext;

        public QuotationService(ApplicationDbContext db, IHubContext<NotificationHub> hubContext)
        {
            _db = db;
            _hubContext = hubContext;
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
    }
}
