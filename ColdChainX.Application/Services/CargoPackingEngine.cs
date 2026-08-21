using System;
using System.Collections.Generic;
using System.Linq;

namespace ColdChainX.Application.Services
{
    public class LpnDims
    {
        public Guid LpnId { get; set; }
        public Guid? PackageLineId { get; set; }
        public string? PackageLabel { get; set; }
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public int RouteStopSequence { get; set; }
        public decimal WeightKg { get; set; }
        public decimal RequiredTemperature { get; set; }
        public bool IsStackable { get; set; } = true;
    }

    public class ContainerDims
    {
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
    }

    public class PlacedItem
    {
        public Guid LpnId { get; set; }
        public Guid? PackageLineId { get; set; }
        public string? PackageLabel { get; set; }
        public int DeliveryStopSequence { get; set; }
        public decimal X { get; set; }
        public decimal Y { get; set; }
        public decimal Z { get; set; }
        public decimal W { get; set; } // width (X)
        public decimal H { get; set; } // height (Y)
        public decimal D { get; set; } // depth (Z)
    }

    public class PackingResult
    {
        public List<PlacedItem> PlacedItems { get; set; } = new();
        public List<Guid> UnplacedLpnIds { get; set; } = new();
        public decimal Utilisation { get; set; }
    }

    public class CargoPackingEngine
    {
        public PackingResult Pack(ContainerDims container, List<LpnDims> itemsToPack)
        {
            var result = new PackingResult();
            
            if (container.Length <= 0 || container.Width <= 0 || container.Height <= 0)
            {
                result.UnplacedLpnIds = itemsToPack.Select(i => i.LpnId).ToList();
                return result;
            }

            decimal usableLength = container.Length - 20m; // Front 10cm + Rear 10cm
            decimal usableHeight = container.Height - 20m; // Top 20cm

            if (usableLength <= 0 || usableHeight <= 0)
            {
                result.UnplacedLpnIds = itemsToPack.Select(i => i.LpnId).ToList();
                return result;
            }

            var sortedItems = itemsToPack
                .OrderByDescending(i => i.RouteStopSequence)
                .ThenBy(i => i.RequiredTemperature) // Colder items first (front)
                .ThenByDescending(i => i.Length * i.Width * i.Height)
                .ThenByDescending(i => CountSameFootprint(itemsToPack, i))
                .ThenByDescending(i => i.WeightKg) // Heavier items first (bottom)
                .ToList();

            var placedItems = new List<PlacedItem>();
            var unplacedIds = new List<Guid>();
            var stackableByLpnId = itemsToPack
                .GroupBy(i => i.LpnId)
                .ToDictionary(g => g.Key, g => g.All(i => i.IsStackable));

            decimal evapH_physical = Math.Min(container.Height * 0.3m, 60m);
            decimal evapD_physical = 30m;
            
            decimal packingEvapH = evapH_physical - 20m;
            decimal packingEvapD = evapD_physical - 10m;

            if (packingEvapH > 0 && packingEvapD > 0)
            {
                placedItems.Add(new PlacedItem
                {
                    LpnId = Guid.Empty, // Dummy ID for obstacle
                    X = 0, // Block full width
                    Y = usableHeight - packingEvapH,
                    Z = 0,
                    W = container.Width,
                    H = packingEvapH,
                    D = packingEvapD
                });
            }

            foreach (var item in sortedItems)
            {
                var orientations = GetOrientations(item);

                var candidatePoints = new List<(decimal x, decimal y, decimal z)> { (0, 0, 0) };

                foreach (var pi in placedItems)
                {
                    candidatePoints.Add((pi.X + pi.W, pi.Y, pi.Z));
                    candidatePoints.Add((pi.X, pi.Y + pi.H, pi.Z));
                    candidatePoints.Add((pi.X, pi.Y, pi.Z + pi.D));
                }

                candidatePoints = candidatePoints
                    .Distinct()
                    .OrderBy(p => p.z)
                    .ThenBy(p => p.y)
                    .ThenBy(p => p.x)
                    .ToList();

                var bestPlacement = default((decimal x, decimal y, decimal z, decimal w, decimal h, decimal d, decimal score)?);
                foreach (var point in candidatePoints)
                {
                    foreach (var (w, h, d) in orientations)
                    {
                        if (point.x + w > container.Width ||
                            point.y + h > usableHeight ||
                            point.z + d > usableLength)
                        {
                            continue;
                        }

                        bool collision = false;
                        foreach (var pi in placedItems)
                        {
                            if (Overlap(point.x, point.y, point.z, w, h, d,
                                        pi.X, pi.Y, pi.Z, pi.W, pi.H, pi.D))
                            {
                                collision = true;
                                break;
                            }
                        }

                        if (!collision && ViolatesStackingPolicy(
                                point.x, point.y, point.z, w, d,
                                placedItems,
                                stackableByLpnId))
                        {
                            collision = true;
                        }

                        if (!collision && !HasSufficientSupport(point.x, point.y, point.z, w, d, placedItems))
                        {
                            collision = true;
                        }

                        if (!collision)
                        {
                            var score = ScorePlacement(point.x, point.y, point.z, w, h, d, placedItems);
                            if (!bestPlacement.HasValue || score < bestPlacement.Value.score)
                            {
                                bestPlacement = (point.x, point.y, point.z, w, h, d, score);
                            }
                        }
                    }
                }

                if (bestPlacement.HasValue)
                {
                    var placement = bestPlacement.Value;
                    placedItems.Add(new PlacedItem
                    {
                        LpnId = item.LpnId,
                        PackageLineId = item.PackageLineId,
                        PackageLabel = item.PackageLabel,
                        DeliveryStopSequence = item.RouteStopSequence,
                        X = placement.x,
                        Y = placement.y,
                        Z = placement.z,
                        W = placement.w,
                        H = placement.h,
                        D = placement.d
                    });
                }
                else
                {
                    unplacedIds.Add(item.LpnId);
                }
            }

            placedItems.RemoveAll(pi => pi.LpnId == Guid.Empty);

            if (placedItems.Any())
            {
                decimal maxPlacedX = placedItems.Max(pi => pi.X + pi.W);
                decimal unusedX = container.Width - maxPlacedX;
                decimal offsetX = unusedX > 0 ? unusedX / 2m : 0;
                
                foreach (var pi in placedItems)
                {
                    if (offsetX > 0) pi.X += offsetX;
                    pi.Z += 10m; // Front Bulkhead clearance
                }
            }

            result.PlacedItems = placedItems;
            result.UnplacedLpnIds = unplacedIds;

            decimal evapH = Math.Min(container.Height * 0.3m, 60m);
            decimal evaporatorVolume = 50m * container.Width * evapH;
            
            decimal usableContainerVolume = (usableLength * container.Width * usableHeight) - evaporatorVolume;
            
            decimal usedVolume = placedItems.Sum(pi => pi.W * pi.H * pi.D);
            result.Utilisation = usableContainerVolume > 0 ? Math.Min((usedVolume / usableContainerVolume) * 100, 100m) : 0;

            return result;
        }

        private List<(decimal w, decimal h, decimal d)> GetOrientations(LpnDims item)
        {
            var l = item.Length;
            var w = item.Width;
            var h = item.Height;

            var list = new List<(decimal w, decimal h, decimal d)>
            {
                (w, h, l),
                (l, h, w),
                (w, l, h),
                (h, l, w),
                (l, w, h),
                (h, w, l)
            };

            return list.Distinct().ToList();
        }

        private int CountSameFootprint(List<LpnDims> items, LpnDims item)
            => items.Count(other =>
                other.Length == item.Length
                && other.Width == item.Width
                && other.Height == item.Height);

        private bool Overlap(decimal x1, decimal y1, decimal z1, decimal w1, decimal h1, decimal d1,
                             decimal x2, decimal y2, decimal z2, decimal w2, decimal h2, decimal d2)
        {
            return x1 < x2 + w2 && x1 + w1 > x2 &&
                   y1 < y2 + h2 && y1 + h1 > y2 &&
                   z1 < z2 + d2 && z1 + d1 > z2;
        }

        private bool ViolatesStackingPolicy(
            decimal x,
            decimal y,
            decimal z,
            decimal w,
            decimal d,
            List<PlacedItem> placedItems,
            IReadOnlyDictionary<Guid, bool> stackableByLpnId)
        {
            if (y <= 0)
            {
                return false;
            }

            foreach (var placed in placedItems)
            {
                if (placed.LpnId == Guid.Empty)
                {
                    continue;
                }

                if (!stackableByLpnId.TryGetValue(placed.LpnId, out var isStackable) || isStackable)
                {
                    continue;
                }

                var hasFootprintOverlap = x < placed.X + placed.W
                    && x + w > placed.X
                    && z < placed.Z + placed.D
                    && z + d > placed.Z;

                if (hasFootprintOverlap && y >= placed.Y + placed.H)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasSufficientSupport(
            decimal x,
            decimal y,
            decimal z,
            decimal w,
            decimal d,
            List<PlacedItem> placedItems)
        {
            if (y <= 0)
            {
                return true;
            }

            var supportedArea = 0m;
            foreach (var placed in placedItems)
            {
                if (placed.Y + placed.H != y)
                {
                    continue;
                }

                var overlapX = Math.Max(0m, Math.Min(x + w, placed.X + placed.W) - Math.Max(x, placed.X));
                var overlapZ = Math.Max(0m, Math.Min(z + d, placed.Z + placed.D) - Math.Max(z, placed.Z));
                supportedArea += overlapX * overlapZ;
            }

            var baseArea = w * d;
            return baseArea > 0 && supportedArea / baseArea >= 0.70m;
        }

        private decimal ScorePlacement(
            decimal x,
            decimal y,
            decimal z,
            decimal w,
            decimal h,
            decimal d,
            List<PlacedItem> placedItems)
        {
            var maxZAfterPlace = Math.Max(z + d, placedItems.Where(p => p.LpnId != Guid.Empty).Select(p => p.Z + p.D).DefaultIfEmpty(0m).Max());
            var maxYAfterPlace = Math.Max(y + h, placedItems.Where(p => p.LpnId != Guid.Empty).Select(p => p.Y + p.H).DefaultIfEmpty(0m).Max());
            var contact = CalculateContactScore(x, y, z, w, h, d, placedItems);

            return (maxZAfterPlace * 1_000_000m)
                + (z * 10_000m)
                + (maxYAfterPlace * 1_000m)
                + (y * 100m)
                + x
                - contact;
        }

        private decimal CalculateContactScore(
            decimal x,
            decimal y,
            decimal z,
            decimal w,
            decimal h,
            decimal d,
            List<PlacedItem> placedItems)
        {
            var contact = 0m;
            foreach (var placed in placedItems)
            {
                if (x == placed.X + placed.W || x + w == placed.X)
                {
                    var overlapY = Math.Max(0m, Math.Min(y + h, placed.Y + placed.H) - Math.Max(y, placed.Y));
                    var overlapZ = Math.Max(0m, Math.Min(z + d, placed.Z + placed.D) - Math.Max(z, placed.Z));
                    contact += overlapY * overlapZ;
                }

                if (y == placed.Y + placed.H || y + h == placed.Y)
                {
                    var overlapX = Math.Max(0m, Math.Min(x + w, placed.X + placed.W) - Math.Max(x, placed.X));
                    var overlapZ = Math.Max(0m, Math.Min(z + d, placed.Z + placed.D) - Math.Max(z, placed.Z));
                    contact += overlapX * overlapZ;
                }

                if (z == placed.Z + placed.D || z + d == placed.Z)
                {
                    var overlapX = Math.Max(0m, Math.Min(x + w, placed.X + placed.W) - Math.Max(x, placed.X));
                    var overlapY = Math.Max(0m, Math.Min(y + h, placed.Y + placed.H) - Math.Max(y, placed.Y));
                    contact += overlapX * overlapY;
                }
            }

            return contact;
        }
    }
}
