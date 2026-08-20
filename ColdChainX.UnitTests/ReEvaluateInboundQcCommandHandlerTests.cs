using ColdChainX.Application.Features.Inbound.Commands;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace ColdChainX.UnitTests;

public sealed class ReEvaluateInboundQcCommandHandlerTests
{
    [Fact]
    public async Task CorrectMeasurements_ReplacesActualLinesAndUpdatesAggregatesAndExistingFinalQuotation()
    {
        using var database = new SqliteTestDatabase();
        var seeded = await SeedScenarioAsync(database.Db);
        var handler = CreateHandler(database.Db);

        var result = await handler.Handle(CreateCorrectionCommand(seeded.LpnId, seeded.WarehouseId), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(11, result.ActualQuantity);
        Assert.Equal(88m, result.ActualWeightKg);
        Assert.Equal(0.3062m, result.ActualCbm);
        Assert.Equal(seeded.QuoteId, result.QuoteId);

        database.Db.ChangeTracker.Clear();
        var lpn = await database.Db.Lpns
            .Include(entity => entity.InboundQcPackageLines)
            .Include(entity => entity.Order)
                .ThenInclude(order => order.OrderDimension)
            .Include(entity => entity.Receipt)
            .SingleAsync(entity => entity.LpnId == seeded.LpnId);
        var quotation = await database.Db.Quotations.SingleAsync(entity => entity.OrderId == seeded.OrderId
            && entity.Status == "FINAL"
            && entity.PricingSource == "AUTO_ACTUAL");

        Assert.Equal(2, lpn.InboundQcPackageLines.Count);
        Assert.DoesNotContain(lpn.InboundQcPackageLines, line => line.InboundQcPackageLineId == seeded.OldPackageLineId);
        Assert.Equal(new[] { "Thùng 10kg", "Thùng 5kg" }, lpn.InboundQcPackageLines.OrderBy(line => line.Label).Select(line => line.Label));
        Assert.Equal(11, lpn.Quantity);
        Assert.Equal(88m, lpn.ActualWeightKg);
        Assert.Equal(0.3062m, lpn.ActualCbm);
        Assert.Null(lpn.LengthCm);
        Assert.Equal(-17.5m, lpn.RecordedTemperature);
        Assert.Equal(10, lpn.Order.Quantity);
        Assert.Equal(88m, lpn.Order.OrderDimension!.ActualWeightKg);
        Assert.Equal(0.3062m, lpn.Order.OrderDimension.ActualCbm);
        Assert.Equal(11m, lpn.Receipt.TotalActualQty);
        Assert.Equal("PENDING_PUTAWAY", lpn.Receipt.ReferenceDocNo);

        Assert.Equal(seeded.QuoteId, quotation.QuoteId);
        Assert.Equal(88_000m, quotation.BaseFreight);
        Assert.Equal(88m, quotation.ChargeableWeightKg);
        Assert.Equal(76.55m, quotation.VolumetricWeightKg);
        Assert.Equal(7_040m, quotation.VatAmount);
        Assert.Equal(95_040m, quotation.FinalAmount);
        Assert.Null(quotation.FileUrl);
        Assert.Single(await database.Db.Quotations.Where(entity => entity.OrderId == seeded.OrderId
            && entity.Status == "FINAL"
            && entity.PricingSource == "AUTO_ACTUAL").ToListAsync());

        var declaredLines = await database.Db.OrderPackageLines
            .Where(line => line.OrderId == seeded.OrderId)
            .ToListAsync();
        declaredLines = declaredLines.OrderBy(line => line.CapacityKg).ToList();
        Assert.Equal(2, declaredLines.Count);
        Assert.Equal(new[] { 4, 6 }, declaredLines.Select(line => line.Quantity));
    }

    [Fact]
    public async Task CorrectMeasurements_WhenReceiptPdfExists_IsRejectedWithoutChanges()
    {
        using var database = new SqliteTestDatabase();
        var seeded = await SeedScenarioAsync(database.Db, receiptPdfUrl: "/receipts/final.pdf");
        var handler = CreateHandler(database.Db);

        var result = await handler.Handle(CreateCorrectionCommand(seeded.LpnId, seeded.WarehouseId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("after the warehouse receipt PDF", result.Message);
        database.Db.ChangeTracker.Clear();
        Assert.Single(await database.Db.InboundQcPackageLines.Where(line => line.LpnId == seeded.LpnId).ToListAsync());
        Assert.Equal(85m, (await database.Db.Lpns.SingleAsync(entity => entity.LpnId == seeded.LpnId)).ActualWeightKg);
        Assert.Equal(85_000m, (await database.Db.Quotations.SingleAsync(entity => entity.QuoteId == seeded.QuoteId)).BaseFreight);
    }

    [Fact]
    public async Task CorrectMeasurements_WhenDatabaseSaveFails_RollsBackEveryPersistedChange()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .EnableDetailedErrors()
            .Options;
        await using var db = new FailingSaveApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var seeded = await SeedScenarioAsync(db);
        db.ThrowAfterSaving = true;
        var handler = CreateHandler(db);

        var result = await handler.Handle(CreateCorrectionCommand(seeded.LpnId, seeded.WarehouseId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No database changes were saved", result.Message);
        db.ChangeTracker.Clear();

        var persistedLines = await db.InboundQcPackageLines.Where(line => line.LpnId == seeded.LpnId).ToListAsync();
        var persistedLpn = await db.Lpns.SingleAsync(entity => entity.LpnId == seeded.LpnId);
        var persistedOrderDimension = await db.Set<OrderDimension>().SingleAsync(entity => entity.OrderId == seeded.OrderId);
        var persistedQuotation = await db.Quotations.SingleAsync(entity => entity.QuoteId == seeded.QuoteId);

        Assert.Single(persistedLines);
        Assert.Equal(seeded.OldPackageLineId, persistedLines[0].InboundQcPackageLineId);
        Assert.Equal(10, persistedLpn.Quantity);
        Assert.Equal(85m, persistedLpn.ActualWeightKg);
        Assert.Equal(85m, persistedOrderDimension.ActualWeightKg);
        Assert.Equal(85_000m, persistedQuotation.BaseFreight);
        Assert.Equal(91_800m, persistedQuotation.FinalAmount);
    }

    [Fact]
    public async Task CorrectMeasurements_WithInvalidPackageLine_IsRejectedBeforeMutation()
    {
        using var database = new SqliteTestDatabase();
        var seeded = await SeedScenarioAsync(database.Db);
        var handler = CreateHandler(database.Db);
        var command = CreateCorrectionCommand(seeded.LpnId, seeded.WarehouseId);
        command.ActualPackageLinesJson = JsonSerializer.Serialize(new[]
        {
            new { label = "Thùng lỗi", quantity = 0, actualWeightKg = 10m, lengthCm = 10m, widthCm = 10m, heightCm = 10m }
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("quantity must be greater than 0", result.Message);
        Assert.Single(await database.Db.InboundQcPackageLines.Where(line => line.LpnId == seeded.LpnId).ToListAsync());
    }

    private static ReEvaluateInboundQcCommandHandler CreateHandler(ApplicationDbContext db)
        => new(db, NullLogger<ReEvaluateInboundQcCommandHandler>.Instance, new FileServiceStub());

    private static ReEvaluateInboundQcCommand CreateCorrectionCommand(Guid lpnId, Guid warehouseId)
        => new()
        {
            LpnId = lpnId,
            WarehouseId = warehouseId,
            Temperature = -17.5m,
            ActualPackageLinesJson = JsonSerializer.Serialize(new[]
            {
                new { label = "Thùng 5kg", quantity = 4, actualWeightKg = 22m, lengthCm = 35m, widthCm = 25m, heightCm = 20m },
                new { label = "Thùng 10kg", quantity = 7, actualWeightKg = 66m, lengthCm = 45m, widthCm = 30m, heightCm = 25m }
            })
        };

    private static async Task<SeededScenario> SeedScenarioAsync(
        ApplicationDbContext db,
        string? receiptPdfUrl = null)
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var warehouse = new Warehouse
        {
            WarehouseId = Guid.NewGuid(),
            WarehouseCode = $"WH-{Guid.NewGuid():N}",
            WarehouseName = "Kho kiểm thử",
            WarehouseType = "COLD_STORAGE",
            MaxPallets = 100,
            Status = "ACTIVE",
            CreatedAt = now
        };
        var receiver = new User
        {
            UserId = Guid.NewGuid(),
            Username = $"qc-{Guid.NewGuid():N}",
            FullName = "Nhân viên kiểm thử",
            Warehouse = warehouse,
            WarehouseId = warehouse.WarehouseId,
            Status = "ACTIVE",
            CreatedAt = now
        };
        var order = new TransportOrder
        {
            OrderId = Guid.NewGuid(),
            TrackingCode = $"ORD-{Guid.NewGuid():N}",
            ItemName = "Cá hồi đông lạnh",
            Category = "MEAT_SEAFOOD",
            Quantity = 10,
            PackingType = "Kiện",
            TempCondition = "-18",
            Status = "RECEIVING",
            CreatedAt = now,
            OrderDimension = new OrderDimension
            {
                ExpectedWeightKg = 80m,
                ActualWeightKg = 85m,
                ExpectedCbm = 0.25m,
                ActualCbm = 0.2725m,
                LengthCm = 0m,
                WidthCm = 0m,
                HeightCm = 0m,
                TotalPackageQuantity = 10
            }
        };
        var asn = new InboundAsn
        {
            AsnId = Guid.NewGuid(),
            AsnCode = $"ASN-{Guid.NewGuid():N}",
            Order = order,
            OrderId = order.OrderId,
            WarehouseId = warehouse.WarehouseId,
            RequestedDropoffTime = now,
            QrCodeValue = $"QR-{Guid.NewGuid():N}",
            Status = "QC_PASSED",
            CreatedAt = now
        };
        var receipt = new WarehouseReceipt
        {
            ReceiptId = Guid.NewGuid(),
            ReceiptCode = $"REC-{Guid.NewGuid():N}",
            ReferenceDocNo = "PENDING_PUTAWAY",
            Order = order,
            OrderId = order.OrderId,
            Warehouse = warehouse,
            WarehouseId = warehouse.WarehouseId,
            ReceiptType = "INBOUND",
            TotalExpectedQty = 10,
            TotalActualQty = 10,
            RecordedTemperature = -18m,
            DelivererName = "Người giao hàng",
            Receiver = receiver,
            ReceiverId = receiver.UserId,
            PdfUrl = receiptPdfUrl,
            CreatedAt = now
        };
        var lpn = new Lpn
        {
            LpnId = Guid.NewGuid(),
            LpnCode = $"LPN-{Guid.NewGuid():N}",
            Order = order,
            OrderId = order.OrderId,
            Receipt = receipt,
            ReceiptId = receipt.ReceiptId,
            Quantity = 10,
            ActualWeightKg = 85m,
            ActualCbm = 0.2725m,
            State = LpnState.RECEIVING,
            RecordedTemperature = -18m,
            CreatedAt = now
        };
        var oldLine = new InboundQcPackageLine
        {
            InboundQcPackageLineId = Guid.NewGuid(),
            Order = order,
            OrderId = order.OrderId,
            Asn = asn,
            AsnId = asn.AsnId,
            Lpn = lpn,
            LpnId = lpn.LpnId,
            Label = "Dữ liệu cũ",
            Quantity = 10,
            ActualWeightKg = 85m,
            LengthCm = 45m,
            WidthCm = 30m,
            HeightCm = 25m,
            ActualCbm = 0.2725m,
            CreatedAt = now
        };
        var quotation = new Quotation
        {
            QuoteId = Guid.NewGuid(),
            Order = order,
            OrderId = order.OrderId,
            BaseFreight = 85_000m,
            SystemBaseFreight = 85_000m,
            ChargeableWeightKg = 85m,
            VolumetricWeightKg = 68.13m,
            PricePerKg = 1_000m,
            VatPercentage = 8m,
            VatAmount = 6_800m,
            FinalAmount = 91_800m,
            PricingSource = "AUTO_ACTUAL",
            Status = "FINAL",
            FileUrl = "/quotation-old.pdf",
            CreatedAt = now
        };
        var declaredFiveKg = new OrderPackageLine
        {
            OrderPackageLineId = Guid.NewGuid(),
            Order = order,
            OrderId = order.OrderId,
            Label = "Thùng 5kg",
            CapacityKg = 5m,
            Quantity = 4,
            CreatedAt = now
        };
        var declaredTenKg = new OrderPackageLine
        {
            OrderPackageLineId = Guid.NewGuid(),
            Order = order,
            OrderId = order.OrderId,
            Label = "Thùng 10kg",
            CapacityKg = 10m,
            Quantity = 6,
            CreatedAt = now
        };

        db.AddRange(warehouse, receiver, order, asn, receipt, lpn, oldLine, quotation, declaredFiveKg, declaredTenKg);
        await db.SaveChangesAsync();
        return new SeededScenario(order.OrderId, lpn.LpnId, warehouse.WarehouseId, quotation.QuoteId, oldLine.InboundQcPackageLineId);
    }

    private sealed record SeededScenario(
        Guid OrderId,
        Guid LpnId,
        Guid WarehouseId,
        Guid QuoteId,
        Guid OldPackageLineId);

    private sealed class FileServiceStub : IFileService
    {
        public string GetSignedUrl(string publicId) => publicId;
        public Task<string> UploadFileAsync(IFormFile file) => Task.FromResult($"/evidence/{file.FileName}");
        public Task<string> UploadFileAsync(Stream stream, string fileName) => Task.FromResult($"/files/{fileName}");
        public Task<string> UploadFileAsync(byte[] fileBytes, string fileName) => Task.FromResult($"/files/{fileName}");
    }
}

internal sealed class FailingSaveApplicationDbContext : ApplicationDbContext
{
    public FailingSaveApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public bool ThrowAfterSaving { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await base.SaveChangesAsync(cancellationToken);
        if (ThrowAfterSaving)
            throw new InvalidOperationException("Simulated failure after database save.");
        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
            property.SetDefaultValueSql(null);
    }
}
