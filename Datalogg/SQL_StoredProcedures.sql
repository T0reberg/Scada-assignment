-- ============================================================
-- SCADA_DB  —  Datalogging stored procedures
-- Run this in SSMS after creating the tables from the schema script
-- ============================================================

USE SCADA_DB;
GO

-- ── Seed the three sensors used by the Datalogging module ────────────────
-- (skip if already inserted)
IF NOT EXISTS (SELECT 1 FROM Sensors WHERE SensorID = 1)
BEGIN
    SET IDENTITY_INSERT Sensors ON;
    INSERT INTO Sensors (SensorID, Name, Unit, MinVal, MaxVal, Location)
    VALUES
        (1, 'Temperature',   '°C', 10.0, 50.0, 'Air heater outlet'),
        (2, 'ControlSignal', 'V',   0.0,  5.0, 'Heater control input'),
        (3, 'Setpoint',      '°C', 20.0, 50.0, 'Operator setpoint');
    SET IDENTITY_INSERT Sensors OFF;
END
GO

-- ── sp_InsertMeasurement ─────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE sp_InsertMeasurement
    @SensorID  INT,
    @Value     FLOAT,
    @Timestamp DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO Measurements (SensorID, Value, Timestamp)
    VALUES (@SensorID, @Value, @Timestamp);
END
GO

-- ── sp_GetRecentMeasurements ─────────────────────────────────────────────
CREATE OR ALTER PROCEDURE sp_GetRecentMeasurements
    @SensorID INT,
    @Count    INT = 200
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Count)
        m.MeasID,
        m.SensorID,
        s.Name,
        s.Unit,
        m.Value,
        m.Timestamp
    FROM Measurements m
    JOIN Sensors s ON m.SensorID = s.SensorID
    WHERE m.SensorID = @SensorID
    ORDER BY m.Timestamp DESC;
END
GO

-- ── sp_GetMeasurementsBetween (useful for paper / analysis) ──────────────
CREATE OR ALTER PROCEDURE sp_GetMeasurementsBetween
    @SensorID  INT,
    @StartTime DATETIME,
    @EndTime   DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        m.MeasID,
        m.SensorID,
        s.Name,
        s.Unit,
        m.Value,
        m.Timestamp
    FROM Measurements m
    JOIN Sensors s ON m.SensorID = s.SensorID
    WHERE m.SensorID  = @SensorID
      AND m.Timestamp BETWEEN @StartTime AND @EndTime
    ORDER BY m.Timestamp ASC;
END
GO
