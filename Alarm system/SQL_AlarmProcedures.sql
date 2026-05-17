-- ============================================================
-- SCADA_DB  —  Alarm System stored procedures + trigger
-- Run in SSMS after the main schema script
-- ============================================================

USE SCADA_DB;
GO

-- ── Seed AlarmConfig thresholds ──────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM AlarmConfig WHERE SensorID = 1 AND AlarmType = 'HIGH')
BEGIN
    INSERT INTO AlarmConfig (SensorID, AlarmType, Threshold, IsEnabled)
    VALUES
        (1, 'HIGH', 45.0, 1),   -- Temperature HIGH  at 45°C
        (1, 'HIHI', 48.0, 1),   -- Temperature HIHI  at 48°C
        (1, 'LOW',  22.0, 1);   -- Temperature LOW   at 22°C
END
GO

-- ── sp_GetActiveAlarms ───────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE sp_GetActiveAlarms
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        a.AlarmID,
        a.SensorID,
        s.Name          AS SensorName,
        a.AlarmType,
        a.Threshold,
        a.TriggeredValue,
        a.TriggeredAt,
        a.AcknowledgedAt,
        a.IsAcknowledged
    FROM Alarms a
    JOIN Sensors s ON a.SensorID = s.SensorID
    WHERE a.IsAcknowledged = 0
    ORDER BY a.TriggeredAt DESC;
END
GO

-- ── sp_GetAlarmHistory ────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE sp_GetAlarmHistory
    @From    DATETIME = NULL,
    @To      DATETIME = NULL,
    @MaxRows INT      = 500
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@MaxRows)
        a.AlarmID,
        a.SensorID,
        s.Name          AS SensorName,
        a.AlarmType,
        a.Threshold,
        a.TriggeredValue,
        a.TriggeredAt,
        a.AcknowledgedAt,
        a.IsAcknowledged
    FROM Alarms a
    JOIN Sensors s ON a.SensorID = s.SensorID
    WHERE (@From IS NULL OR a.TriggeredAt >= @From)
      AND (@To   IS NULL OR a.TriggeredAt <= @To)
    ORDER BY a.TriggeredAt DESC;
END
GO

-- ── sp_AcknowledgeAlarm ───────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE sp_AcknowledgeAlarm
    @AlarmID        INT,
    @AcknowledgedAt DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Alarms
    SET IsAcknowledged = 1,
        AcknowledgedAt = @AcknowledgedAt
    WHERE AlarmID = @AlarmID;
END
GO

-- ── sp_AcknowledgeAll ─────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE sp_AcknowledgeAll
    @AcknowledgedAt DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Alarms
    SET IsAcknowledged = 1,
        AcknowledgedAt = @AcknowledgedAt
    WHERE IsAcknowledged = 0;
    SELECT @@ROWCOUNT AS AcknowledgedCount;
END
GO

-- ── sp_GetAlarmStats ──────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE sp_GetAlarmStats
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        (SELECT COUNT(*) FROM Alarms WHERE IsAcknowledged = 0)                            AS ActiveCount,
        (SELECT COUNT(*) FROM Alarms WHERE CAST(TriggeredAt AS DATE) = CAST(GETDATE() AS DATE)) AS TodayCount,
        (SELECT COUNT(*) FROM Alarms)                                                     AS TotalCount;
END
GO

-- ── sp_GetAlarmConfig ─────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE sp_GetAlarmConfig
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        ac.ConfigID,
        ac.SensorID,
        s.Name  AS SensorName,
        ac.AlarmType,
        ac.Threshold,
        ac.IsEnabled
    FROM AlarmConfig ac
    JOIN Sensors s ON ac.SensorID = s.SensorID
    ORDER BY ac.SensorID, ac.AlarmType;
END
GO

-- ── Alarm trigger on Measurements ────────────────────────────────────────
-- Fires after every INSERT into Measurements.
-- Checks HIGH, HIHI, and LOW thresholds from AlarmConfig.
CREATE OR ALTER TRIGGER trg_AlarmCheck
ON Measurements
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    -- HIGH and HIHI alarms (value too high)
    INSERT INTO Alarms (SensorID, AlarmType, Threshold, TriggeredValue, TriggeredAt, IsAcknowledged)
    SELECT
        i.SensorID,
        ac.AlarmType,
        ac.Threshold,
        i.Value,
        GETDATE(),
        0
    FROM inserted i
    JOIN AlarmConfig ac ON ac.SensorID = i.SensorID
    WHERE ac.IsEnabled = 1
      AND i.Value > ac.Threshold
      AND ac.AlarmType IN ('HIGH', 'HIHI')
      -- Don't repeat the same alarm if one is already active
      AND NOT EXISTS (
          SELECT 1 FROM Alarms a
          WHERE a.SensorID     = i.SensorID
            AND a.AlarmType    = ac.AlarmType
            AND a.IsAcknowledged = 0
      );

    -- LOW alarms (value too low)
    INSERT INTO Alarms (SensorID, AlarmType, Threshold, TriggeredValue, TriggeredAt, IsAcknowledged)
    SELECT
        i.SensorID,
        ac.AlarmType,
        ac.Threshold,
        i.Value,
        GETDATE(),
        0
    FROM inserted i
    JOIN AlarmConfig ac ON ac.SensorID = i.SensorID
    WHERE ac.IsEnabled = 1
      AND i.Value < ac.Threshold
      AND ac.AlarmType = 'LOW'
      AND NOT EXISTS (
          SELECT 1 FROM Alarms a
          WHERE a.SensorID     = i.SensorID
            AND a.AlarmType    = ac.AlarmType
            AND a.IsAcknowledged = 0
      );
END
GO
