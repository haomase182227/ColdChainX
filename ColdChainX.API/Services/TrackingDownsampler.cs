namespace ColdChainX.API.Services;

public sealed record TrackingPoint(
    DateTime Timestamp,
    decimal TempC,
    decimal Lat,
    decimal Lon);

public static class TrackingDownsampler
{
    public static IReadOnlyList<TrackingPoint> Downsample(
        IReadOnlyList<TrackingPoint> points,
        int maxPoints)
    {
        if (maxPoints <= 0 || points.Count <= maxPoints || maxPoints < 3)
        {
            return points;
        }

        var sampled = new List<TrackingPoint>(maxPoints) { points[0] };
        var bucketSize = (double)(points.Count - 2) / (maxPoints - 2);
        var anchorIndex = 0;

        for (var bucket = 0; bucket < maxPoints - 2; bucket++)
        {
            var rangeStart = (int)Math.Floor(bucket * bucketSize) + 1;
            var rangeEnd = (int)Math.Floor((bucket + 1) * bucketSize) + 1;
            rangeEnd = Math.Min(rangeEnd, points.Count - 1);

            var nextRangeStart = (int)Math.Floor((bucket + 1) * bucketSize) + 1;
            var nextRangeEnd = (int)Math.Floor((bucket + 2) * bucketSize) + 1;
            nextRangeEnd = Math.Min(nextRangeEnd, points.Count);

            var avgX = 0d;
            var avgY = 0d;
            var nextCount = Math.Max(1, nextRangeEnd - nextRangeStart);
            for (var i = nextRangeStart; i < nextRangeEnd; i++)
            {
                avgX += ToUnixSeconds(points[i].Timestamp);
                avgY += (double)points[i].TempC;
            }
            avgX /= nextCount;
            avgY /= nextCount;

            var anchor = points[anchorIndex];
            var anchorX = ToUnixSeconds(anchor.Timestamp);
            var anchorY = (double)anchor.TempC;

            var maxArea = -1d;
            var selectedIndex = rangeStart;
            for (var i = rangeStart; i < rangeEnd; i++)
            {
                var area = Math.Abs(
                    (anchorX - avgX) * ((double)points[i].TempC - anchorY) -
                    (anchorX - ToUnixSeconds(points[i].Timestamp)) * (avgY - anchorY));

                if (area > maxArea)
                {
                    maxArea = area;
                    selectedIndex = i;
                }
            }

            sampled.Add(points[selectedIndex]);
            anchorIndex = selectedIndex;
        }

        sampled.Add(points[^1]);
        return sampled;
    }

    private static double ToUnixSeconds(DateTime timestamp)
    {
        var utc = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }
}
