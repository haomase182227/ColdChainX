using System.Runtime.CompilerServices;
using ColdChainX.Application.Features.Inbound.Commands;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace ColdChainX.UnitTests;

public class InboundQcPackageGroupingTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public InboundQcPackageGroupingTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ProcessQc_SameGroupKey_CreatesOneLpnContainingTwoSizeLines()
    {
        var orderId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var smallId = Guid.NewGuid();
        var largeId = Guid.NewGuid();

        _db.Users.Add(new User
        {
            UserId = receiverId,
            Username = "qc-worker",
            FullName = "QC Worker",
            Status = "ACTIVE",
            WarehouseId = warehouseId
        });
        _db.Warehouses.Add(new Warehouse
        {
            WarehouseId = warehouseId,
            WarehouseCode = "WH-QC",
            WarehouseName = "QC Warehouse",
            WarehouseType = "HUB",
            Address = "Test address",
            MaxPallets = 100,
            Status = "ACTIVE"
        });
        _db.TransportOrders.Add(new TransportOrder
        {
            OrderId = orderId,
            TrackingCode = "TRK-QC-GROUP-01",
            ItemName = "Mango",
            Category = "FOOD",
            Quantity = 15,
            PackingType = "Box, Crate",
            TempCondition = "5",
            Status = "CONTRACT_SIGNED",
            OrderDimension = new OrderDimension
            {
                OrderId = orderId,
                ExpectedWeightKg = 190m,
                ActualWeightKg = 190m,
                ExpectedCbm = 1.1m,
                ActualCbm = 1.1m,
                LengthCm = 40m,
                WidthCm = 30m,
                HeightCm = 25m
            },
            PackageVariants = new List<OrderPackageVariant>
            {
                new()
                {
                    OrderPackageVariantId = smallId,
                    OrderId = orderId,
                    VariantName = "Small",
                    PackingType = "Box",
                    Quantity = 10,
                    ExpectedUnitWeightKg = 8m,
                    ExpectedTotalWeightKg = 80m,
                    ExpectedCbm = 0.3m,
                    LengthCm = 40m,
                    WidthCm = 30m,
                    HeightCm = 25m,
                    CreatedAt = DateTime.UtcNow
                },
                new()
                {
                    OrderPackageVariantId = largeId,
                    OrderId = orderId,
                    VariantName = "Large",
                    PackingType = "Crate",
                    Quantity = 5,
                    ExpectedUnitWeightKg = 22m,
                    ExpectedTotalWeightKg = 110m,
                    ExpectedCbm = 0.8m,
                    LengthCm = 80m,
                    WidthCm = 50m,
                    HeightCm = 40m,
                    CreatedAt = DateTime.UtcNow
                }
            }
        });
        _db.InboundAsns.Add(new InboundAsn
        {
            AsnId = asnId,
            AsnCode = "ASN-QC-01",
            OrderId = orderId,
            RequestedDropoffTime = DateTime.UtcNow,
            QrCodeValue = "ASN-QC-01",
            Status = "SCHEDULED",
            WarehouseId = warehouseId,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var handler = new ProcessInboundQcCommandHandler(
            _db,
            NullLogger<ProcessInboundQcCommandHandler>.Instance,
            new FakeFileService(),
            new FakeMediator(),
            null!);

        var result = await handler.Handle(new ProcessInboundQcCommand
        {
            AsnId = asnId,
            ReceiverId = receiverId,
            WarehouseId = warehouseId,
            PackageMeasurements = new List<PackageVariantQcMeasurement>
            {
                new()
                {
                    OrderPackageVariantId = smallId,
                    LpnGroupKey = "PALLET-A",
                    Quantity = 10,
                    ActualWeightKg = 80m,
                    LengthCm = 40m,
                    WidthCm = 30m,
                    HeightCm = 25m,
                    Temperature = 5m
                },
                new()
                {
                    OrderPackageVariantId = largeId,
                    LpnGroupKey = "PALLET-A",
                    Quantity = 5,
                    ActualWeightKg = 110m,
                    LengthCm = 80m,
                    WidthCm = 50m,
                    HeightCm = 40m,
                    Temperature = 5m
                }
            }
        }, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var lpn = Assert.Single(await _db.Lpns.Include(item => item.PackageVariantLines).ToListAsync());
        Assert.Equal(15, lpn.Quantity);
        Assert.Equal(190m, lpn.ActualWeightKg);
        Assert.Equal(1.1m, lpn.ActualCbm);
        Assert.Equal(2, lpn.PackageVariantLines.Count);
        Assert.Single(result.Lpns);
        Assert.Equal(2, result.Lpns[0].PackageLines.Count);
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeFileService : IFileService
    {
        public Task<string> UploadFileAsync(IFormFile file) => Task.FromResult("https://example.test/evidence.jpg");
        public Task<string> UploadFileAsync(Stream stream, string fileName) => Task.FromResult($"https://example.test/{fileName}");
        public Task<string> UploadFileAsync(byte[] fileBytes, string fileName) => Task.FromResult($"https://example.test/{fileName}");
        public Task DeleteFileAsync(string fileUrl) => Task.CompletedTask;
        public string GetSignedUrl(string publicId) => $"https://example.test/{publicId}";
    }

    private sealed class FakeMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object? result = typeof(TResponse) == typeof(byte[]) ? new byte[] { 1, 2, 3 } : default(TResponse);
            return Task.FromResult((TResponse)result!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(request is IRequest<byte[]> ? new byte[] { 1, 2, 3 } : null);

        public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<object?> CreateStream(
            object request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;
    }
}
