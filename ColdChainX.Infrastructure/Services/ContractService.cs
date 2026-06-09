using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.Contracts;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Infrastructure.Services
{
    public class ContractService : IContractService
    {
        private const string AcceptedQuote = "ACCEPTED";
        private const string PendingSignature = "PENDING_SIGNATURE";
        private const string Active = "ACTIVE";
        private const string ContractSigned = "CONTRACT_SIGNED";

        private readonly ApplicationDbContext _db;
        private readonly IPdfService _pdfService;
        private readonly IWebHostEnvironment _environment;
        private readonly IHubContext<NotificationHub> _hubContext;

        public ContractService(
            ApplicationDbContext db,
            IPdfService pdfService,
            IWebHostEnvironment environment,
            IHubContext<NotificationHub> hubContext)
        {
            _db = db;
            _pdfService = pdfService;
            _environment = environment;
            _hubContext = hubContext;
        }

        public async Task<ApiResponse<string>> PreviewContractAsync(Guid orderId)
        {
            var data = await LoadContractDataAsync(orderId);
            if (data == null)
                return ApiResponse<string>.Failure("Order, customer, or accepted quotation was not found");

            var template = await LoadTemplateAsync();
            var contractNumber = await GenerateUniqueContractNumberAsync();
            return ApiResponse<string>.SuccessResponse(RenderTemplate(template, data, contractNumber), "Contract preview generated");
        }

        public async Task<ApiResponse<GenerateContractResponse>> GenerateContractAsync(GenerateContractRequest request)
        {
            if (request.OrderId == Guid.Empty)
                return ApiResponse<GenerateContractResponse>.Failure("orderId is required");

            if (string.IsNullOrWhiteSpace(request.EditedHtmlContent))
                return ApiResponse<GenerateContractResponse>.Failure("editedHtmlContent is required");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var data = await LoadContractDataAsync(request.OrderId);
                if (data == null)
                    return ApiResponse<GenerateContractResponse>.Failure("Order, customer, or accepted quotation was not found");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var existingPending = await _db.CustomerContracts
                    .AnyAsync(c => c.OrderId == request.OrderId && c.Status == PendingSignature);
                if (existingPending)
                    return ApiResponse<GenerateContractResponse>.Failure("Order already has a pending signature contract");

                var contractNumber = await GenerateUniqueContractNumberAsync();
                var fileUrl = await _pdfService.SaveContractPdfAsync(request.EditedHtmlContent, contractNumber);
                var contract = new CustomerContract
                {
                    ContractId = Guid.NewGuid(),
                    CustomerId = data.Order.CustomerId,
                    OrderId = data.Order.OrderId,
                    ContractNumber = contractNumber,
                    SignedDate = null,
                    ExpiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                    FileUrl = fileUrl,
                    Status = PendingSignature,
                    CreatedAt = DbNow()
                };

                _db.CustomerContracts.Add(contract);

                var customerUserId = await ResolveCustomerUserIdAsync(data.Order.CustomerId);
                await AddNotificationAsync(
                    customerUserId,
                    request.SalesUserId,
                    "NOTI_CONTRACT_PENDING_SIGNATURE",
                    data.Order.OrderId,
                    new
                    {
                        Contract_Number = contract.ContractNumber,
                        Tracking_Code = data.Order.TrackingCode,
                        File_URL = contract.FileUrl
                    });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _hubContext.Clients.User(data.Order.CustomerId.ToString()!).SendAsync("ContractPendingSignature", new
                {
                    contract.ContractId,
                    contract.ContractNumber,
                    contract.FileUrl,
                    contract.Status,
                    data.Order.OrderId
                });

                return ApiResponse<GenerateContractResponse>.SuccessResponse(new GenerateContractResponse
                {
                    ContractId = contract.ContractId,
                    OrderId = data.Order.OrderId,
                    ContractNumber = contract.ContractNumber,
                    FileUrl = contract.FileUrl,
                    Status = contract.Status!
                }, "Contract generated and sent for signature");
            });
        }

        public async Task<ApiResponse<ApproveContractResponse>> ApproveContractAsync(Guid contractId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var contract = await _db.CustomerContracts
                    .Include(c => c.Customer)
                    .Include(c => c.Order)
                    .FirstOrDefaultAsync(c => c.ContractId == contractId);

                if (contract == null)
                    return ApiResponse<ApproveContractResponse>.Failure("Contract not found");

                if (!string.Equals(contract.Status, PendingSignature, StringComparison.OrdinalIgnoreCase))
                    return ApiResponse<ApproveContractResponse>.Failure("Contract is not pending signature");

                if (contract.Order == null)
                    return ApiResponse<ApproveContractResponse>.Failure("Contract order was not found");

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var trackingCode = await GenerateUniqueTrackingCodeAsync();
                contract.Status = Active;
                contract.SignedDate = DateOnly.FromDateTime(DateTime.UtcNow);
                contract.Order.TrackingCode = trackingCode;
                contract.Order.Status = ContractSigned;

                var salesUserId = await ResolveSalesUserIdAsync();
                var customerUserId = await ResolveCustomerUserIdAsync(contract.CustomerId);
                if (!customerUserId.HasValue)
                    return ApiResponse<ApproveContractResponse>.Failure("Customer user was not found for required document creation");

                await AddRequiredTransportDocumentsAsync(contract.Order.OrderId, customerUserId.Value);

                await AddNotificationAsync(
                    salesUserId,
                    customerUserId,
                    "NOTI_CONTRACT_APPROVED_SALES",
                    contract.Order.OrderId,
                    new { Contract_Number = contract.ContractNumber, Tracking_Code = trackingCode });

                await AddNotificationAsync(
                    customerUserId,
                    null,
                    "NOTI_CONTRACT_APPROVED_CUSTOMER",
                    contract.Order.OrderId,
                    new { Contract_Number = contract.ContractNumber, Tracking_Code = trackingCode });

                await AddNotificationAsync(
                    customerUserId,
                    null,
                    "NOTI_REQUIRED_DOCUMENTS_UPLOAD",
                    contract.Order.OrderId,
                    new
                    {
                        Tracking_Code = trackingCode,
                        Required_Documents = "VAT_INVOICE, DELIVERY_NOTE, INTERNAL_TRANSFER"
                    });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _hubContext.Clients.Group("Group_Sales").SendAsync("ContractApproved", new
                {
                    contract.ContractId,
                    contract.ContractNumber,
                    contract.Order.OrderId,
                    TrackingCode = trackingCode
                });

                await _hubContext.Clients.User(contract.CustomerId.ToString()!).SendAsync("TrackingIssued", new
                {
                    contract.ContractId,
                    contract.Order.OrderId,
                    TrackingCode = trackingCode
                });

                return ApiResponse<ApproveContractResponse>.SuccessResponse(new ApproveContractResponse
                {
                    ContractId = contract.ContractId,
                    OrderId = contract.Order.OrderId,
                    ContractNumber = contract.ContractNumber,
                    ContractStatus = contract.Status!,
                    OrderStatus = contract.Order.Status,
                    TrackingCode = trackingCode,
                    SignedDate = contract.SignedDate
                }, "Contract approved and tracking code issued");
            });
        }

        private async Task<ContractData?> LoadContractDataAsync(Guid orderId)
        {
            var order = await _db.TransportOrders
                .Include(o => o.Customer)
                .Include(o => o.Quotations)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order?.Customer == null)
                return null;

            var quotation = order.Quotations
                .Where(q => string.Equals(q.Status, AcceptedQuote, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            if (quotation == null)
                return null;

            return new ContractData(order, order.Customer, quotation);
        }

        private async Task<string> LoadTemplateAsync()
        {
            var path = Path.Combine(_environment.ContentRootPath, "Templates", "ProshipContractTemplate.html");
            if (!File.Exists(path))
                throw new InvalidOperationException("ProshipContractTemplate.html was not found");

            return await File.ReadAllTextAsync(path);
        }

        private static string RenderTemplate(string template, ContractData data, string contractNumber)
        {
            var now = DateTime.UtcNow;
            var replacements = new Dictionary<string, string?>
            {
                ["Contract_Number"] = contractNumber,
                ["Day"] = now.Day.ToString("00", CultureInfo.InvariantCulture),
                ["Month"] = now.Month.ToString("00", CultureInfo.InvariantCulture),
                ["Year"] = now.Year.ToString(CultureInfo.InvariantCulture),
                ["Customer_CompanyName"] = data.Customer.CompanyName,
                ["Customer_Address"] = data.Customer.Address ?? string.Empty,
                ["Customer_TaxCode"] = data.Customer.TaxCode,
                ["Customer_Phone"] = string.Empty,
                ["Customer_Email"] = data.Customer.Email ?? string.Empty,
                ["Item_Name"] = data.Order.ItemName,
                ["Category"] = data.Order.Category,
                ["Actual_Weight_KG"] = data.Order.ActualWeightKg.ToString("0.##", CultureInfo.InvariantCulture),
                ["Actual_CBM"] = (data.Order.ActualCbm ?? data.Order.ExpectedCbm).ToString("0.####", CultureInfo.InvariantCulture),
                ["Final_Amount"] = data.Quotation.FinalAmount.ToString("N0", CultureInfo.InvariantCulture)
            };

            foreach (var replacement in replacements)
                template = template.Replace($"{{{{{replacement.Key}}}}}", replacement.Value ?? string.Empty);

            return template;
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

        private async Task<string> GenerateUniqueTrackingCodeAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                var value = $"PROSHIP-{DateTime.UtcNow:yyyy}{Random.Shared.Next(0, 999999):D6}";
                if (!await _db.TransportOrders.AnyAsync(o => o.TrackingCode == value))
                    return value;
            }

            return $"PROSHIP-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
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

            await EnsureNotificationTemplateAsync(templateId);

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

        private async Task AddRequiredTransportDocumentsAsync(Guid orderId, Guid uploadedBy)
        {
            var requiredTypes = new[]
            {
                "VAT_INVOICE",
                "DELIVERY_NOTE",
                "INTERNAL_TRANSFER"
            };

            var existingTypes = await _db.TransportDocuments
                .Where(d => d.OrderId == orderId && requiredTypes.Contains(d.DocType))
                .Select(d => d.DocType)
                .ToListAsync();

            foreach (var docType in requiredTypes.Except(existingTypes))
            {
                _db.TransportDocuments.Add(new TransportDocument
                {
                    DocId = Guid.NewGuid(),
                    OrderId = orderId,
                    DocType = docType,
                    ImageUrl = string.Empty,
                    Status = "PENDING",
                    UploadedBy = uploadedBy,
                    CreatedAt = DbNow()
                });
            }
        }

        private async Task EnsureNotificationTemplateAsync(string templateId)
        {
            if (await _db.NotificationTemplates.AnyAsync(t => t.TemplateId == templateId))
                return;

            var typeId = await _db.Messagetypes
                .Where(t => t.TypeName == "ORDER_STATUS")
                .Select(t => (Guid?)t.TypeId)
                .FirstOrDefaultAsync();

            if (!typeId.HasValue)
            {
                var type = new Messagetype
                {
                    TypeId = Guid.NewGuid(),
                    TypeName = "ORDER_STATUS",
                    Description = "Cập nhật trạng thái đơn hàng, báo giá, hợp đồng"
                };
                _db.Messagetypes.Add(type);
                typeId = type.TypeId;
            }

            var (title, body) = templateId switch
            {
                "NOTI_CONTRACT_PENDING_SIGNATURE" => (
                    "Hợp đồng {{Contract_Number}} đang chờ ký",
                    "Hợp đồng {{Contract_Number}} đã được tạo. Vui lòng xem file và ký duyệt để hệ thống cấp mã tracking."),
                "NOTI_CONTRACT_APPROVED_SALES" => (
                    "Khách hàng đã ký hợp đồng {{Contract_Number}}",
                    "Hợp đồng {{Contract_Number}} đã được ký. Mã tracking đã cấp: {{Tracking_Code}}."),
                "NOTI_CONTRACT_APPROVED_CUSTOMER" => (
                    "Đơn hàng đã được kích hoạt: {{Tracking_Code}}",
                    "Hợp đồng {{Contract_Number}} đã có hiệu lực. Mã tracking của bạn là {{Tracking_Code}}."),
                "NOTI_REQUIRED_DOCUMENTS_UPLOAD" => (
                    "Vui lòng bổ sung chứng từ cho đơn {{Tracking_Code}}",
                    "Đơn {{Tracking_Code}} cần upload các chứng từ: {{Required_Documents}} trước khi xe khởi hành."),
                _ => (
                    "Cập nhật hợp đồng {{Contract_Number}}",
                    "Hợp đồng {{Contract_Number}} có cập nhật mới.")
            };

            _db.NotificationTemplates.Add(new NotificationTemplate
            {
                TemplateId = templateId,
                TypeId = typeId.Value,
                TitleTemplate = title,
                BodyTemplate = body,
                Channel = "IN_APP",
                Status = Active
            });
        }

        private static DateTime DbNow()
            => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        private sealed record ContractData(TransportOrder Order, Customer Customer, Quotation Quotation);
    }
}
