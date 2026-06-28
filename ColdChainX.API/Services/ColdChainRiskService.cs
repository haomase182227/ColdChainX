using System.Text.Json;
using ColdChainX.Core.Entities;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.TimeSeries;

namespace ColdChainX.API.Services;

public interface IColdChainRiskService
{
    ColdChainRiskResult Evaluate(MasterTrip trip, IReadOnlyCollection<TransportOrder> orders, double actualTempC, DateTimeOffset timestamp);

    Task<ColdChainModelTrainingResult> TrainModelAsync(bool overwrite, CancellationToken cancellationToken);

    Task<ColdChainSsaTrainingResult> TrainSsaModelAsync(bool overwrite, CancellationToken cancellationToken);

    TemperatureForecastResult ForecastTemperature(IReadOnlyList<double> recentTemperatures, int horizon);

    ColdChainExpertAssessment AssessExpertSystem(
        ColdChainRiskResult risk,
        TemperatureForecastResult forecast,
        GeoFenceAssessment? geoFence,
        double thermalVelocityCPerMin,
        bool doorOpen,
        bool isGraceMuted);
}

public sealed class ColdChainRiskService : IColdChainRiskService
{
    private const int SsaForecastHorizon = 30;

    private readonly object _predictionLock = new();
    private readonly object _ssaLock = new();
    private readonly MLContext _mlContext = new(seed: 42);
    private PredictionEngine<ColdChainRiskModelInput, ColdChainRiskModelOutput>? _predictionEngine;
    private ITransformer? _ssaModel;
    private readonly ILogger<ColdChainRiskService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _dataPath;
    private readonly string _modelPath;
    private readonly string _ssaDataPath;
    private readonly string _ssaModelPath;
    private readonly CargoKnowledgeBase _knowledgeBase;

    public ColdChainRiskService(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<ColdChainRiskService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var mlRoot = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            "..",
            "ColdChainX.Infrastructure",
            "ML"));

        _dataPath = Path.Combine(mlRoot, "Data", "frozen_storage_data_20k.csv");
        _modelPath = Path.Combine(mlRoot, "Models", "coldchain_risk_model.zip");
        _ssaDataPath = Path.Combine(mlRoot, "Data", "temperature_historical_data.csv");
        _ssaModelPath = Path.Combine(mlRoot, "Models", "coldchain_temperature_ssa_model.zip");
        _knowledgeBase = CargoKnowledgeBase.Load(
            Path.Combine(mlRoot, "Config", "cargo_knowledge_base.json"),
            _logger);

        _predictionEngine = TryLoadOrTrainModel(allowTraining: _configuration.GetValue("ML:TrainRiskModelAtRuntime", true), overwrite: false)?.PredictionEngine;
        _ssaModel = TryLoadOrTrainSsaModel(allowTraining: _configuration.GetValue("ML:TrainSsaModelAtRuntime", true), overwrite: false)?.Model;
    }

    public ColdChainRiskResult Evaluate(
        MasterTrip trip,
        IReadOnlyCollection<TransportOrder> orders,
        double actualTempC,
        DateTimeOffset timestamp)
    {
        var profiles = orders
            .Select(o => _knowledgeBase.Resolve(o.Category))
            .ToList();

        if (profiles.Count == 0)
        {
            profiles.Add(_knowledgeBase.DefaultProfile);
        }

        var violationMinutes = CalculateViolationMinutes(trip, timestamp);
        var worst = profiles
            .Select(profile => EvaluateProfile(profile, actualTempC, violationMinutes, timestamp))
            .OrderByDescending(r => r.RiskScore)
            .First();

        return worst;
    }

    private ColdChainRiskResult EvaluateProfile(
        CargoProfile profile,
        double actualTempC,
        float violationMinutes,
        DateTimeOffset timestamp)
    {
        var deviation = Math.Max(0, actualTempC - (double)profile.MaxTempC);
        var input = new ColdChainRiskModelInput
        {
            CargoCategory = profile.Category,
            TempDeviation = (float)deviation,
            DurationMins = deviation > 0 ? violationMinutes : 0,
            ActualTemp = (float)actualTempC,
            ThermalShockRate = violationMinutes > 0 ? (float)(deviation / violationMinutes) : 0,
            ResultLabel = "OK"
        };

        var prediction = Predict(input);
        var label = deviation <= 0 ? "OK" : prediction?.PredictedLabel ?? ClassifyByDeviation(deviation);
        var probability = prediction?.Score is { Length: > 0 } scores ? scores.Max() : 0;
        var score = deviation <= 0
            ? 0
            : Math.Max(MapLabelToScore(label), Math.Round((decimal)probability * 100m, 2));

        return new ColdChainRiskResult
        {
            CargoCategory = profile.Category,
            RequiredMinTempC = profile.MinTempC,
            RequiredMaxTempC = profile.MaxTempC,
            StrictMaxSlopeCPerMin = profile.StrictMaxSlopeCPerMin,
            ActualTempC = (decimal)Math.Round(actualTempC, 2),
            TempDeviationC = (decimal)Math.Round(deviation, 2),
            DurationMins = Math.Round((decimal)input.DurationMins, 2),
            ThermalShockRate = Math.Round((decimal)input.ThermalShockRate, 5),
            RiskScore = score,
            PredictedLabel = label,
            ModelSource = _predictionEngine == null ? "fallback-rule" : "mlnet",
            EvaluatedAt = timestamp
        };
    }

    public Task<ColdChainModelTrainingResult> TrainModelAsync(bool overwrite, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = TryLoadOrTrainModel(allowTraining: true, overwrite: overwrite);
            if (result == null)
            {
                return new ColdChainModelTrainingResult
                {
                    Success = false,
                    Message = "Training failed. Check API logs for details.",
                    DataPath = _dataPath,
                    ModelPath = _modelPath
                };
            }

            lock (_predictionLock)
            {
                _predictionEngine = result.PredictionEngine;
            }

            result.Success = true;
            return result;
        }, cancellationToken);
    }

    public Task<ColdChainSsaTrainingResult> TrainSsaModelAsync(bool overwrite, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = TryLoadOrTrainSsaModel(allowTraining: true, overwrite: overwrite);
            if (result == null)
            {
                return new ColdChainSsaTrainingResult
                {
                    Success = false,
                    Message = "SSA training failed. Check API logs for details.",
                    DataPath = _ssaDataPath,
                    ModelPath = _ssaModelPath
                };
            }

            lock (_ssaLock)
            {
                _ssaModel = result.Model;
            }

            result.Success = true;
            return result;
        }, cancellationToken);
    }

    public TemperatureForecastResult ForecastTemperature(IReadOnlyList<double> recentTemperatures, int horizon)
    {
        var safeHorizon = Math.Clamp(horizon, 1, SsaForecastHorizon);
        if (recentTemperatures.Count == 0)
        {
            return TemperatureForecastResult.Empty("no-data");
        }

        ITransformer? model;
        lock (_ssaLock)
        {
            model = _ssaModel;
        }

        if (model == null)
        {
            return ForecastByLinearTrend(recentTemperatures, safeHorizon);
        }

        try
        {
            var engine = model.CreateTimeSeriesEngine<TemperatureForecastRow, TemperatureForecastOutput>(_mlContext);
            TemperatureForecastOutput? output = null;
            foreach (var temp in recentTemperatures.TakeLast(240))
            {
                output = engine.Predict(new TemperatureForecastRow { TempC = (float)temp });
            }

            var forecast = output?.ForecastedTemp?.Take(safeHorizon).ToArray() ?? Array.Empty<float>();
            if (forecast.Length == 0)
            {
                return ForecastByLinearTrend(recentTemperatures, safeHorizon);
            }

            return new TemperatureForecastResult
            {
                ModelSource = "mlnet-ssa",
                HorizonMinutes = forecast.Length,
                CurrentTempC = Math.Round(recentTemperatures[^1], 2),
                ForecastTempC = forecast.Select(v => Math.Round((double)v, 2)).ToArray(),
                MaxForecastTempC = Math.Round(forecast.Max(), 2),
                MinForecastTempC = Math.Round(forecast.Min(), 2),
                UpperBoundTempC = output?.UpperBoundTemp?.Take(forecast.Length).Select(v => Math.Round((double)v, 2)).ToArray() ?? Array.Empty<double>(),
                LowerBoundTempC = output?.LowerBoundTemp?.Take(forecast.Length).Select(v => Math.Round((double)v, 2)).ToArray() ?? Array.Empty<double>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSA temperature forecast failed; falling back to linear trend.");
            return ForecastByLinearTrend(recentTemperatures, safeHorizon);
        }
    }

    public ColdChainExpertAssessment AssessExpertSystem(
        ColdChainRiskResult risk,
        TemperatureForecastResult forecast,
        GeoFenceAssessment? geoFence,
        double thermalVelocityCPerMin,
        bool doorOpen,
        bool isGraceMuted)
    {
        var reasons = new List<string>();
        var actions = new List<string>();
        var score = risk.RiskScore;
        var velocity = (decimal)Math.Round(thermalVelocityCPerMin, 4);
        var strictSlope = risk.StrictMaxSlopeCPerMin;

        if (isGraceMuted)
        {
            return new ColdChainExpertAssessment
            {
                Severity = "MUTED",
                SmartRiskScore = 0,
                RootCause = "DELIVERY_GRACE_PERIOD",
                ThermalVelocityCPerMin = velocity,
                StrictMaxSlopeCPerMin = strictSlope,
                Reasons = new[] { "AI alerting is muted during the 20-minute post-delivery door-close grace period." },
                RecommendedActions = new[] { "Continue telemetry logging and resume alerting after the grace period." },
                RequiresHumanReview = false,
                IsGraceMuted = true
            };
        }

        if (risk.TempDeviationC > 0)
        {
            reasons.Add($"Temperature exceeds {risk.CargoCategory} limit by {risk.TempDeviationC:0.##}C.");
            actions.Add("Inspect refrigeration unit and verify cargo compartment temperature.");
        }

        var slopeViolation = strictSlope > 0 && velocity > strictSlope;
        if (slopeViolation)
        {
            score = Math.Max(score, doorOpen ? 82 : 72);
            reasons.Add($"Thermal velocity {velocity:0.####}C/min exceeds strict max slope {strictSlope:0.####}C/min.");
        }

        if (forecast.ForecastTempC.Count > 0 && forecast.MaxForecastTempC > (double)risk.RequiredMaxTempC)
        {
            var forecastDeviation = (decimal)Math.Round(forecast.MaxForecastTempC - (double)risk.RequiredMaxTempC, 2);
            score = Math.Max(score, forecastDeviation >= 3 ? 80 : 55);
            reasons.Add($"SSA forecast predicts a temperature breach within {forecast.HorizonMinutes} minutes.");
            actions.Add("Pre-cool route stop plan or dispatch support before temperature crosses SLA.");
        }

        if (geoFence is { IsHardViolation: true })
        {
            score = Math.Max(score, 85);
            reasons.Add("Vehicle is outside the hard geofence layer for the planned route.");
            actions.Add("Contact driver immediately and validate route deviation.");
        }
        else if (geoFence is { IsSoftViolation: true })
        {
            score = Math.Max(score, 60);
            reasons.Add("Vehicle is outside the soft geofence layer for the planned route.");
            actions.Add("Monitor route deviation and request driver confirmation.");
        }

        if (score >= 90)
        {
            actions.Add("Escalate to operations manager and prepare claim evidence.");
        }

        var rootCause = DiagnoseRootCause(
            risk,
            forecast,
            geoFence,
            slopeViolation,
            doorOpen);
        actions.AddRange(BuildRootCauseActions(rootCause));

        var severity = score >= 90 ? "CRITICAL"
            : score >= 70 ? "HIGH"
            : score >= 40 ? "WATCH"
            : "NORMAL";

        return new ColdChainExpertAssessment
        {
            Severity = severity,
            SmartRiskScore = Math.Round(score, 2),
            RootCause = rootCause,
            ThermalVelocityCPerMin = velocity,
            StrictMaxSlopeCPerMin = strictSlope,
            Reasons = reasons,
            RecommendedActions = actions,
            RequiresHumanReview = score >= 70 || geoFence?.IsHardViolation == true
        };
    }

    private static string DiagnoseRootCause(
        ColdChainRiskResult risk,
        TemperatureForecastResult forecast,
        GeoFenceAssessment? geoFence,
        bool slopeViolation,
        bool doorOpen)
    {
        if (geoFence is { IsHardViolation: true })
        {
            return "ROUTE_DEVIATION";
        }

        if (doorOpen && (slopeViolation || risk.TempDeviationC > 0))
        {
            return "HEAT_LEAK_DOOR_OPEN";
        }

        if (!doorOpen && slopeViolation)
        {
            return "REFRIGERATION_DEGRADATION";
        }

        if (!doorOpen && forecast.ForecastTempC.Count > 0 && forecast.MaxForecastTempC > (double)risk.RequiredMaxTempC)
        {
            return "REFRIGERATION_DEGRADATION";
        }

        if (risk.TempDeviationC > 0)
        {
            return "TEMPERATURE_LIMIT_BREACH";
        }

        return "NO_ACTIVE_RISK";
    }

    private static IEnumerable<string> BuildRootCauseActions(string rootCause)
    {
        return rootCause switch
        {
            "HEAT_LEAK_DOOR_OPEN" => new[] { "Ask driver to inspect door seal and close cargo door immediately." },
            "REFRIGERATION_DEGRADATION" => new[] { "Check compressor setpoint, refrigerant system, and reefer power status." },
            "ROUTE_DEVIATION" => new[] { "Validate current road route and stop plan with dispatch." },
            "TEMPERATURE_LIMIT_BREACH" => new[] { "Verify probe placement and cargo compartment airflow." },
            _ => Array.Empty<string>()
        };
    }

    private ColdChainModelTrainingResult? TryLoadOrTrainModel(bool allowTraining, bool overwrite)
    {
        try
        {
            if (!overwrite && File.Exists(_modelPath))
            {
                var loadedModel = _mlContext.Model.Load(_modelPath, out _);
                _logger.LogInformation("Cold-chain ML.NET risk model loaded from {ModelPath}", _modelPath);
                return new ColdChainModelTrainingResult
                {
                    Success = true,
                    Message = "Model loaded from existing file.",
                    DataPath = _dataPath,
                    ModelPath = _modelPath,
                    WasTrained = false,
                    PredictionEngine = _mlContext.Model.CreatePredictionEngine<ColdChainRiskModelInput, ColdChainRiskModelOutput>(loadedModel)
                };
            }

            if (!File.Exists(_dataPath))
            {
                _logger.LogWarning("Cold-chain risk dataset not found at {DataPath}; fallback risk rules will be used.", _dataPath);
                return null;
            }

            if (!allowTraining)
            {
                _logger.LogWarning(
                    "Cold-chain ML.NET risk model was not found at {ModelPath}, and runtime training is disabled by ML:TrainRiskModelAtRuntime=false. Fallback risk rules will be used.",
                    _modelPath);
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);

            var trainingData = _mlContext.Data.LoadFromTextFile<ColdChainRiskTrainingRow>(
                _dataPath,
                hasHeader: true,
                separatorChar: ',');

            var split = _mlContext.Data.TrainTestSplit(trainingData, testFraction: 0.2, seed: 42);
            var pipeline = _mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(ColdChainRiskTrainingRow.ResultLabel))
                .Append(_mlContext.Transforms.Categorical.OneHotEncoding("CargoCategoryEncoded", nameof(ColdChainRiskTrainingRow.CargoCategory)))
                .Append(_mlContext.Transforms.Concatenate(
                    "Features",
                    "CargoCategoryEncoded",
                    nameof(ColdChainRiskTrainingRow.TempDeviation),
                    nameof(ColdChainRiskTrainingRow.DurationMins),
                    nameof(ColdChainRiskTrainingRow.ActualTemp),
                    nameof(ColdChainRiskTrainingRow.ThermalShockRate)))
                .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue(nameof(ColdChainRiskModelOutput.PredictedLabel), "PredictedLabel"));

            var model = pipeline.Fit(split.TrainSet);
            var predictions = model.Transform(split.TestSet);
            var metrics = _mlContext.MulticlassClassification.Evaluate(predictions);

            _mlContext.Model.Save(model, trainingData.Schema, _modelPath);

            _logger.LogInformation(
                "Cold-chain ML.NET risk model trained and saved to {ModelPath}. MicroAccuracy={MicroAccuracy:0.###}, MacroAccuracy={MacroAccuracy:0.###}",
                _modelPath,
                metrics.MicroAccuracy,
                metrics.MacroAccuracy);

            return new ColdChainModelTrainingResult
            {
                Success = true,
                Message = "Model trained and saved.",
                DataPath = _dataPath,
                ModelPath = _modelPath,
                WasTrained = true,
                MicroAccuracy = metrics.MicroAccuracy,
                MacroAccuracy = metrics.MacroAccuracy,
                LogLoss = metrics.LogLoss,
                PredictionEngine = _mlContext.Model.CreatePredictionEngine<ColdChainRiskModelInput, ColdChainRiskModelOutput>(model)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cold-chain ML.NET risk model could not be loaded or trained; fallback risk rules will be used.");
            return null;
        }
    }

    private ColdChainSsaTrainingResult? TryLoadOrTrainSsaModel(bool allowTraining, bool overwrite)
    {
        try
        {
            if (!overwrite && File.Exists(_ssaModelPath))
            {
                var loadedModel = _mlContext.Model.Load(_ssaModelPath, out _);
                _logger.LogInformation("Cold-chain SSA temperature model loaded from {ModelPath}", _ssaModelPath);
                return new ColdChainSsaTrainingResult
                {
                    Success = true,
                    Message = "SSA model loaded from existing file.",
                    DataPath = _ssaDataPath,
                    ModelPath = _ssaModelPath,
                    WasTrained = false,
                    Model = loadedModel
                };
            }

            if (!allowTraining)
            {
                _logger.LogWarning(
                    "Cold-chain SSA model was not found at {ModelPath}, and runtime training is disabled.",
                    _ssaModelPath);
                return null;
            }

            if (!File.Exists(_ssaDataPath))
            {
                _logger.LogWarning("Cold-chain SSA dataset not found at {DataPath}.", _ssaDataPath);
                return null;
            }

            var rows = _mlContext.Data
                .CreateEnumerable<TemperatureForecastRow>(
                    _mlContext.Data.LoadFromTextFile<TemperatureForecastRow>(
                        _ssaDataPath,
                        hasHeader: true,
                        separatorChar: ','),
                    reuseRowObject: false)
                .ToList();

            if (rows.Count < 120)
            {
                _logger.LogWarning("Cold-chain SSA dataset is too small: {Count} rows.", rows.Count);
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_ssaModelPath)!);

            var trainingData = _mlContext.Data.LoadFromEnumerable(rows);
            var windowSize = Math.Min(60, Math.Max(12, rows.Count / 20));
            var seriesLength = Math.Min(rows.Count, Math.Max(windowSize * 4, 120));
            var pipeline = _mlContext.Forecasting.ForecastBySsa(
                outputColumnName: nameof(TemperatureForecastOutput.ForecastedTemp),
                inputColumnName: nameof(TemperatureForecastRow.TempC),
                windowSize: windowSize,
                seriesLength: seriesLength,
                trainSize: rows.Count,
                horizon: SsaForecastHorizon,
                confidenceLevel: 0.95f,
                confidenceLowerBoundColumn: nameof(TemperatureForecastOutput.LowerBoundTemp),
                confidenceUpperBoundColumn: nameof(TemperatureForecastOutput.UpperBoundTemp));

            var model = pipeline.Fit(trainingData);
            _mlContext.Model.Save(model, trainingData.Schema, _ssaModelPath);

            _logger.LogInformation(
                "Cold-chain SSA temperature model trained and saved to {ModelPath}. Rows={Rows}, Window={Window}, Series={Series}",
                _ssaModelPath,
                rows.Count,
                windowSize,
                seriesLength);

            return new ColdChainSsaTrainingResult
            {
                Success = true,
                Message = "SSA model trained and saved.",
                DataPath = _ssaDataPath,
                ModelPath = _ssaModelPath,
                WasTrained = true,
                RowCount = rows.Count,
                WindowSize = windowSize,
                SeriesLength = seriesLength,
                Horizon = SsaForecastHorizon,
                Model = model
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cold-chain SSA model could not be loaded or trained.");
            return null;
        }
    }

    private static TemperatureForecastResult ForecastByLinearTrend(IReadOnlyList<double> recentTemperatures, int horizon)
    {
        var tail = recentTemperatures.TakeLast(Math.Min(12, recentTemperatures.Count)).ToArray();
        var current = tail[^1];
        var slope = tail.Length >= 2 ? (tail[^1] - tail[0]) / (tail.Length - 1) : 0;
        var forecast = Enumerable.Range(1, horizon)
            .Select(step => Math.Round(current + slope * step, 2))
            .ToArray();

        return new TemperatureForecastResult
        {
            ModelSource = "fallback-linear-trend",
            HorizonMinutes = horizon,
            CurrentTempC = Math.Round(current, 2),
            ForecastTempC = forecast,
            MaxForecastTempC = forecast.Max(),
            MinForecastTempC = forecast.Min(),
            UpperBoundTempC = forecast.Select(v => Math.Round(v + 0.75, 2)).ToArray(),
            LowerBoundTempC = forecast.Select(v => Math.Round(v - 0.75, 2)).ToArray()
        };
    }

    private ColdChainRiskModelOutput? Predict(ColdChainRiskModelInput input)
    {
        if (_predictionEngine == null)
        {
            return null;
        }

        lock (_predictionLock)
        {
            return _predictionEngine.Predict(input);
        }
    }

    private static float CalculateViolationMinutes(MasterTrip trip, DateTimeOffset timestamp)
    {
        var startedAt = trip.StartedAt ?? trip.PlannedStartTime;
        if (startedAt == default)
        {
            return 1;
        }

        var startedOffset = new DateTimeOffset(DateTime.SpecifyKind(startedAt, DateTimeKind.Utc));
        var minutes = Math.Max(1, (timestamp.ToUniversalTime() - startedOffset).TotalMinutes);
        return (float)Math.Min(minutes, 24 * 60);
    }

    private static string ClassifyByDeviation(double deviation)
    {
        if (deviation >= 10)
        {
            return "CRITICAL_DAMAGE_RISK";
        }

        if (deviation >= 5)
        {
            return "HIGH_RISK";
        }

        return "WATCH";
    }

    private static decimal MapLabelToScore(string label)
    {
        var normalized = label.ToUpperInvariant();
        if (normalized.Contains("HONG") || normalized.Contains("HOAN_TOAN"))
        {
            return 95m;
        }

        if (normalized.Contains("CHAY") || normalized.Contains("DICH") || normalized.Contains("RA_DONG"))
        {
            return 75m;
        }

        if (normalized.Contains("NHUN") || normalized.Contains("NAT"))
        {
            return 60m;
        }

        if (normalized.Contains("WATCH"))
        {
            return 35m;
        }

        return 40m;
    }

    private static string NormalizeCategory(string value)
    {
        var normalized = value.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');

        if (normalized.Contains("ICE") || normalized.Contains("KEM") || normalized.Contains("JUICE"))
        {
            return "ICE_CREAM_JUICE";
        }

        if (normalized.Contains("MEAT") || normalized.Contains("SEAFOOD") || normalized.Contains("THIT") || normalized.Contains("CA"))
        {
            return "FROZEN_MEAT_SEAFOOD";
        }

        if (normalized.Contains("FRUIT") || normalized.Contains("VEG") || normalized.Contains("RAU") || normalized.Contains("TRAI"))
        {
            return "FROZEN_FRUITS";
        }

        return normalized.Length == 0 ? "UNKNOWN" : normalized;
    }

    private sealed class CargoKnowledgeBase
    {
        private readonly List<CargoProfile> _profiles;
        private readonly Dictionary<string, CargoProfile> _profileIndex;

        private CargoKnowledgeBase(CargoProfile defaultProfile, List<CargoProfile> profiles)
        {
            DefaultProfile = defaultProfile;
            _profiles = profiles;
            _profileIndex = profiles
                .SelectMany(profile => profile.LookupKeys.Select(key => new { Key = key, Profile = profile }))
                .GroupBy(item => item.Key)
                .ToDictionary(group => group.Key, group => group.First().Profile);
        }

        public CargoProfile DefaultProfile { get; }

        public CargoProfile Resolve(string? rawCategory)
        {
            var normalized = NormalizeCategory(rawCategory ?? string.Empty);
            if (_profileIndex.TryGetValue(normalized, out var exact))
            {
                return exact;
            }

            var fuzzy = _profiles.FirstOrDefault(profile =>
                profile.LookupKeys.Any(key => normalized.Contains(key, StringComparison.OrdinalIgnoreCase)));

            return fuzzy ?? DefaultProfile;
        }

        public static CargoKnowledgeBase Load(string path, ILogger logger)
        {
            try
            {
                if (File.Exists(path))
                {
                    var document = JsonSerializer.Deserialize<CargoKnowledgeBaseDocument>(
                        File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (document?.DefaultProfile != null && document.Profiles.Count > 0)
                    {
                        var defaultProfile = document.DefaultProfile.ToProfile();
                        var profiles = document.Profiles.Select(p => p.ToProfile()).ToList();
                        logger.LogInformation("Cargo knowledge base loaded from {Path} with {Count} profiles.", path, profiles.Count);
                        return new CargoKnowledgeBase(defaultProfile, profiles);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cargo knowledge base could not be loaded from {Path}; using built-in fallback.", path);
            }

            var fallback = new CargoProfile("UNKNOWN", 0, 25, 0.35m, "Built-in fallback profile", Array.Empty<string>());
            return new CargoKnowledgeBase(fallback, new List<CargoProfile> { fallback });
        }
    }

    private sealed record CargoKnowledgeBaseDocument
    {
        public CargoProfileDocument? DefaultProfile { get; init; }

        public List<CargoProfileDocument> Profiles { get; init; } = new();
    }

    private sealed record CargoProfileDocument
    {
        public string Category { get; init; } = "UNKNOWN";

        public List<string> Aliases { get; init; } = new();

        public decimal MinTempC { get; init; }

        public decimal MaxTempC { get; init; }

        public decimal StrictMaxSlopeCPerMin { get; init; }

        public string Description { get; init; } = string.Empty;

        public CargoProfile ToProfile()
        {
            return new CargoProfile(Category, MinTempC, MaxTempC, StrictMaxSlopeCPerMin, Description, Aliases);
        }
    }

    private sealed record CargoProfile(
        string Category,
        decimal MinTempC,
        decimal MaxTempC,
        decimal StrictMaxSlopeCPerMin,
        string Description,
        IReadOnlyCollection<string> Aliases)
    {
        public IReadOnlyCollection<string> LookupKeys
        {
            get
            {
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    NormalizeCategory(Category)
                };

                foreach (var alias in Aliases)
                {
                    keys.Add(NormalizeCategory(alias));
                }

                return keys;
            }
        }
    }
}

public sealed class ColdChainRiskResult
{
    public string CargoCategory { get; set; } = string.Empty;

    public decimal RequiredMinTempC { get; set; }

    public decimal RequiredMaxTempC { get; set; }

    public decimal StrictMaxSlopeCPerMin { get; set; }

    public decimal ActualTempC { get; set; }

    public decimal TempDeviationC { get; set; }

    public decimal DurationMins { get; set; }

    public decimal ThermalShockRate { get; set; }

    public decimal RiskScore { get; set; }

    public string PredictedLabel { get; set; } = string.Empty;

    public string ModelSource { get; set; } = string.Empty;

    public DateTimeOffset EvaluatedAt { get; set; }
}

public sealed class ColdChainModelTrainingResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string DataPath { get; set; } = string.Empty;

    public string ModelPath { get; set; } = string.Empty;

    public bool WasTrained { get; set; }

    public double? MicroAccuracy { get; set; }

    public double? MacroAccuracy { get; set; }

    public double? LogLoss { get; set; }

    internal PredictionEngine<ColdChainRiskModelInput, ColdChainRiskModelOutput>? PredictionEngine { get; set; }
}

public sealed class ColdChainSsaTrainingResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string DataPath { get; set; } = string.Empty;

    public string ModelPath { get; set; } = string.Empty;

    public bool WasTrained { get; set; }

    public int RowCount { get; set; }

    public int WindowSize { get; set; }

    public int SeriesLength { get; set; }

    public int Horizon { get; set; }

    internal ITransformer? Model { get; set; }
}

public sealed class TemperatureForecastResult
{
    public string ModelSource { get; set; } = string.Empty;

    public int HorizonMinutes { get; set; }

    public double CurrentTempC { get; set; }

    public IReadOnlyList<double> ForecastTempC { get; set; } = Array.Empty<double>();

    public double MaxForecastTempC { get; set; }

    public double MinForecastTempC { get; set; }

    public IReadOnlyList<double> UpperBoundTempC { get; set; } = Array.Empty<double>();

    public IReadOnlyList<double> LowerBoundTempC { get; set; } = Array.Empty<double>();

    public static TemperatureForecastResult Empty(string source)
    {
        return new TemperatureForecastResult { ModelSource = source };
    }
}

public sealed class ColdChainExpertAssessment
{
    public string Severity { get; set; } = "NORMAL";

    public decimal SmartRiskScore { get; set; }

    public string RootCause { get; set; } = "NO_ACTIVE_RISK";

    public decimal ThermalVelocityCPerMin { get; set; }

    public decimal StrictMaxSlopeCPerMin { get; set; }

    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> RecommendedActions { get; set; } = Array.Empty<string>();

    public bool RequiresHumanReview { get; set; }

    public bool IsGraceMuted { get; set; }
}

public sealed class GeoFenceAssessment
{
    public string Layer { get; set; } = "UNKNOWN";

    public bool IsSoftViolation { get; set; }

    public bool IsHardViolation { get; set; }

    public double NearestDistanceMeters { get; set; }

    public double SoftRadiusMeters { get; set; }

    public double HardRadiusMeters { get; set; }

    public string NearestLocation { get; set; } = string.Empty;

    public string DistanceSource { get; set; } = "haversine";
}

public sealed class ColdChainRiskTrainingRow
{
    [LoadColumn(2)]
    public string CargoCategory { get; set; } = string.Empty;

    [LoadColumn(4)]
    public float TempDeviation { get; set; }

    [LoadColumn(5)]
    public float DurationMins { get; set; }

    [LoadColumn(3)]
    public float ActualTemp { get; set; }

    [LoadColumn(6)]
    public float ThermalShockRate { get; set; }

    [LoadColumn(8)]
    public string ResultLabel { get; set; } = string.Empty;
}

public sealed class TemperatureForecastRow
{
    [LoadColumn(1)]
    public float TempC { get; set; }
}

public sealed class TemperatureForecastOutput
{
    public float[] ForecastedTemp { get; set; } = Array.Empty<float>();

    public float[] UpperBoundTemp { get; set; } = Array.Empty<float>();

    public float[] LowerBoundTemp { get; set; } = Array.Empty<float>();
}

public sealed class ColdChainRiskModelInput
{
    public string CargoCategory { get; set; } = string.Empty;

    public float TempDeviation { get; set; }

    public float DurationMins { get; set; }

    public float ActualTemp { get; set; }

    public float ThermalShockRate { get; set; }

    public string ResultLabel { get; set; } = "OK";
}

public sealed class ColdChainRiskModelOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();
}
