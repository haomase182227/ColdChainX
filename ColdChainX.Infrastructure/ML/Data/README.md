# Frozen Storage Risk Dataset

`frozen_storage_data_10k.csv` is the training dataset for the ML.NET cold-chain risk classifier.

Schema:

- `CargoCategory`: cargo group label.
- `TempDeviation`: absolute temperature deviation from the allowed range.
- `DurationMins`: violation duration in minutes.
- `ActualTemp`: measured temperature.
- `ThermalShock_Rate`: temperature change rate.
- `ResultLabel`: expected risk class.

Runtime behavior:

- `ColdChainRiskService` loads `ML/Models/coldchain_risk_model.zip` when it exists.
- If the model file does not exist, the service trains a multiclass ML.NET model from this CSV and saves the zip model automatically.
- The monitoring flow still falls back to rule-based scoring if the CSV/model cannot be loaded, so telemetry ingestion does not stop.
