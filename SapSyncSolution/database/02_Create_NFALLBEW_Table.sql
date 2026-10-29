/*
    SAP IS-H N_FALL_BEW (Case Movements with Extended Attributes) -> SQL Server target table.
    Extends NBEW with movement category & type details, movement timing, and location mappings.
    Column names match the "SyncTables" section of appsettings.json (SAP field -> SQL column).
    SAP types: CHAR/NUMC -> NVARCHAR, DATS -> DATE, TIMS -> TIME(0).
    Initial SAP dates (00000000) are stored as NULL.
    The script is idempotent: existing table is left untouched.
*/
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'dbo') IS NULL
    EXEC (N'CREATE SCHEMA [dbo] AUTHORIZATION [dbo];');
GO

/* N_FALL_BEW - תזוזת המקרים המפורטת (detailed case movements) */
IF OBJECT_ID(N'[dbo].[N_FALL_BEW]', N'U') IS NULL
CREATE TABLE [dbo].[N_FALL_BEW]
(
    [MANDT]           NVARCHAR(3)      NOT NULL,    -- MANDT        Client
    [FALNR]           NVARCHAR(10)     NOT NULL,    -- FALNR        Case Number (מספר מקרה)
    [LFDBEW]          NVARCHAR(10)     NOT NULL,    -- LFDBEW       Movement Sequence Number (sequence within case)
    [PATNR]           NVARCHAR(10)     NULL,        -- PATNR        Patient ID (מספר מטופל)
    [BEWTY]           NVARCHAR(2)      NULL,        -- BEWTY        Movement Category (קטגוריית תזוזה)
                                                     --   '1' = Admission (קבלה)
                                                     --   '2' = Discharge (שחרור)
                                                     --   '3' = Internal Transfer (העברה)
                                                     --   '4' = Outpatient Visit (ביקור אמבולטורי)
                                                     --   '6' = Absence Start (תחילת היעדרות)
                                                     --   '7' = Absence End (סיום היעדרות)
    [BWART]           NVARCHAR(4)      NULL,        -- BWART        Movement Type (סוג תזוזה בתוך קטגוריה)
    [BWIDT]           DATE             NULL,        -- BWIDT        Movement Date (תאריך תזוזה)
    [BWITM]           TIME(0)          NULL,        -- BWITM        Movement Time (שעת תזוזה)
    [PLANB]           NVARCHAR(1)      NULL,        -- PLANB        Planned Movement Flag (תזוזה מתוכננת)
    [STORN]           NVARCHAR(1)      NULL,        -- STORN        Movement Canceled Flag (תזוזה בוטלה)
    [FSTORN]          NVARCHAR(1)      NULL,        -- FSTORN       Case Canceled Flag (מקרה בוטל)
    [FALAR]           NVARCHAR(2)      NULL,        -- FALAR        Case Type
                                                     --   '1' = Inpatient (אשפוז)
                                                     --   '2' = Outpatient (אמבולטורי)
                                                     --   '3' = Emergency (מיון)
    [ORGFA]           NVARCHAR(8)      NULL,        -- ORGFA        Medical Department OU (מחלקה רפואית)
    [ORGPF]           NVARCHAR(8)      NULL,        -- ORGPF        Nursing / Ward OU (מחלקה סיעודית)
    [ZIMMR]           NVARCHAR(6)      NULL,        -- ZIMMR        Room Number (חדר)
    [BETT]            NVARCHAR(4)      NULL,        -- BETT         Bed Number (מיטה)
    [CLASS]           NVARCHAR(2)      NULL,        -- CLASS        Patient Type / Billing Classification
    [MovementMeaning] NVARCHAR(150)    NULL,        -- Calculated BEWTY + BWART decoded description (מיקום תזוזה)
    [SyncedAt]        DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),  -- UTC timestamp of sync
    
    CONSTRAINT [PK_sap_N_FALL_BEW] PRIMARY KEY CLUSTERED ([MANDT], [FALNR], [LFDBEW])
);
GO

/* Non-clustered indexes for common queries */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sap_N_FALL_BEW_FALNR_STORN' AND object_id = OBJECT_ID(N'[dbo].[N_FALL_BEW]'))
    CREATE INDEX [IX_sap_N_FALL_BEW_FALNR_STORN] ON [dbo].[N_FALL_BEW] ([FALNR], [STORN], [FSTORN]) INCLUDE ([LFDBEW], [BWIDT], [BEWTY], [BWART]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sap_N_FALL_BEW_FALAR_BEWTY' AND object_id = OBJECT_ID(N'[dbo].[N_FALL_BEW]'))
    CREATE INDEX [IX_sap_N_FALL_BEW_FALAR_BEWTY] ON [dbo].[N_FALL_BEW] ([FALAR], [BEWTY], [BWART]) INCLUDE ([BWIDT]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sap_N_FALL_BEW_BWIDT' AND object_id = OBJECT_ID(N'[dbo].[N_FALL_BEW]'))
    CREATE INDEX [IX_sap_N_FALL_BEW_BWIDT] ON [dbo].[N_FALL_BEW] ([BWIDT]) INCLUDE ([FALNR], [BEWTY], [STORN]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sap_N_FALL_BEW_SyncedAt' AND object_id = OBJECT_ID(N'[dbo].[N_FALL_BEW]'))
    CREATE INDEX [IX_sap_N_FALL_BEW_SyncedAt] ON [dbo].[N_FALL_BEW] ([SyncedAt]) INCLUDE ([FALNR], [STORN]);
GO

/* PATIENT_MONITORING - aggregated patient dashboard state (real-time ARAN monitoring) */
IF OBJECT_ID(N'[dbo].[PATIENT_MONITORING]', N'U') IS NULL
CREATE TABLE [dbo].[PATIENT_MONITORING]
(
    [CaseNumber]          NVARCHAR(10)     NOT NULL PRIMARY KEY,  -- FALNR from N_FALL_BEW
    [AccidentNo]          NVARCHAR(12)     NULL,                   -- UNFNR from ZNACCIDENT (ARAN ID)
    [PatientID]           NVARCHAR(10)     NULL,                   -- PATNR
    [CaseType]            NVARCHAR(2)      NULL,                   -- FALAR (1/2/3)
    [CurrentMovement]     NVARCHAR(150)    NULL,                   -- Resolved BEWTY+BWART description
    [CurrentMovementCode] NVARCHAR(6)      NULL,                   -- BEWTY+BWART composite
    [CurrentLocation]     NVARCHAR(100)    NULL,                   -- Room + Bed descriptor
    [TriageStatus]        NVARCHAR(10)     NULL,                   -- RED/YELLOW/GREEN/BLACK (manual override or from ZNMCICASE.ZNMEDSTATUS)
    [PatientType]         NVARCHAR(50)     NULL,                   -- CLASS decoded (אזרח, צה״ל, etc)
    [LastMovementTime]    DATETIME2(3)     NULL,                   -- BWIDT + BWITM
    [UpdatedAt]           DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    
    CONSTRAINT [FK_PATIENT_MONITORING_CaseNumber] FOREIGN KEY ([CaseNumber]) REFERENCES [dbo].[NFAL] ([PATCASEID])
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PATIENT_MONITORING_AccidentNo' AND object_id = OBJECT_ID(N'[dbo].[PATIENT_MONITORING]'))
    CREATE INDEX [IX_PATIENT_MONITORING_AccidentNo] ON [dbo].[PATIENT_MONITORING] ([AccidentNo]) INCLUDE ([TriageStatus], [UpdatedAt]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PATIENT_MONITORING_UpdatedAt' AND object_id = OBJECT_ID(N'[dbo].[PATIENT_MONITORING]'))
    CREATE INDEX [IX_PATIENT_MONITORING_UpdatedAt] ON [dbo].[PATIENT_MONITORING] ([UpdatedAt]) INCLUDE ([TriageStatus], [CurrentMovement]);
GO
