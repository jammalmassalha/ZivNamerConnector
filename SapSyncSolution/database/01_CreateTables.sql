/*
    SAP IS-H (Namer) -> SQL Server target tables.
    Column names match the "SyncTables" section of appsettings.json (SAP field -> SQL column).
    SAP types: CHAR/NUMC -> NVARCHAR (Hebrew text, leading zeros kept), DATS -> DATE, TIMS -> TIME(0).
    Initial SAP dates (00000000) are stored as NULL.
    The script is idempotent: existing tables are left untouched.
*/
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'dbo') IS NULL
    EXEC (N'CREATE SCHEMA [dbo] AUTHORIZATION [dbo];');
GO

/* NPAT - טבלת המטופלים (patients) */
IF OBJECT_ID(N'[dbo].[NPAT]', N'U') IS NULL
CREATE TABLE [dbo].[NPAT]
(
    [PATIENTID]       NVARCHAR(10) NOT NULL,   -- PATNR       מספר מטופל
    [ID_NUMBER]       NVARCHAR(20) NULL,       -- EXTNR       מספר זהות
    [LAST_NAME_PAT]   NVARCHAR(30) NULL,       -- NNAME       שם משפחה
    [FRST_NAME_PAT]   NVARCHAR(30) NULL,       -- VNAME       שם פרטי
    [SEX]             NVARCHAR(1)  NULL,       -- GSCHL       מין
    [DOB]             DATE         NULL,       -- GBDAT       תאריך לידה
    [BIRTHCTRY]       NVARCHAR(3)  NULL,       -- GLAND       ארץ לידה
    [MAR_STAT]        NVARCHAR(1)  NULL,       -- FAMST       מצב משפחתי
    [REL_DENOM]       NVARCHAR(2)  NULL,       -- KONFE       דת
    [DT_O_DEATH_FROM] DATE         NULL,       -- TODDT       תאריך פטירה
    [CITY]            NVARCHAR(25) NULL,       -- ORT         עיר
    [PHONE]           NVARCHAR(16) NULL,       -- TELF1       טלפון
    [HMO]             NVARCHAR(6)  NULL,       -- ZNKUPA_NOZZ קופ"ח
    CONSTRAINT [PK_sap_NPAT] PRIMARY KEY CLUSTERED ([PATIENTID])
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sap_NPAT_ID_NUMBER' AND object_id = OBJECT_ID(N'[dbo].[NPAT]'))
    CREATE INDEX [IX_sap_NPAT_ID_NUMBER] ON [dbo].[NPAT] ([ID_NUMBER]);
GO

/* NFAL - טבלת המקרים (cases) */
IF OBJECT_ID(N'[dbo].[NFAL]', N'U') IS NULL
CREATE TABLE [dbo].[NFAL]
(
    [PATCASEID]     NVARCHAR(10) NOT NULL,     -- FALNR מספר מקרה
    [PATIENTID]     NVARCHAR(10) NULL,         -- PATNR מספר מטופל
    [CASETYPE]      NVARCHAR(1)  NULL,         -- FALAR סוג מקרה
    [CREATION_DATE] DATE         NULL,         -- ERDAT נוצר בתאריך
    [CANCEL_USER]   NVARCHAR(12) NULL,         -- STUSR בוטל ע"י
    [CANCEL_DATE]   DATE         NULL,         -- STDAT בוטל בתאריך
    CONSTRAINT [PK_sap_NFAL] PRIMARY KEY CLUSTERED ([PATCASEID])
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sap_NFAL_PATIENTID' AND object_id = OBJECT_ID(N'[dbo].[NFAL]'))
    CREATE INDEX [IX_sap_NFAL_PATIENTID] ON [dbo].[NFAL] ([PATIENTID]);
GO

/* NBEW - תזוזת המקרים (case movements) */
IF OBJECT_ID(N'[dbo].[NBEW]', N'U') IS NULL
CREATE TABLE [dbo].[NBEW]
(
    [PATCASEID]     NVARCHAR(10) NOT NULL,     -- FALNR מספר מקרה
    [MOVEMNT_SEQNO] NVARCHAR(5)  NOT NULL,     -- LFDNR מספר סידורי של תנועה (NUMC, leading zeros kept)
    [MOVEMNT_DATE]  DATE         NULL,         -- BWIDT תאריך העברה
    [MOVEMNT_TIME]  TIME(0)      NULL,         -- BWIZT שעת העברה
    [MOVEMNT_CTGRY] NVARCHAR(1)  NULL,         -- BEWTY קטגוריית העברה
    [ACCIDENT_NO]   NVARCHAR(12) NULL,         -- UNFNR מספר קובץ / אירוע
    [DEPARTMENT]    NVARCHAR(8)  NULL,         -- ORGFA מחלקה רפואית עבור מקרה
    [NURS_TREAT_OU] NVARCHAR(8)  NULL,         -- ORGPF מחלקה סיעודית עבור מקרה
    [CANCEL_DATE]   DATE         NULL,         -- STDAT תאריך ביטול
    [CANCEL_USER]   NVARCHAR(12) NULL,         -- STUSR בוטל ע"י
    CONSTRAINT [PK_sap_NBEW] PRIMARY KEY CLUSTERED ([PATCASEID], [MOVEMNT_SEQNO])
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sap_NBEW_ACCIDENT_NO' AND object_id = OBJECT_ID(N'[dbo].[NBEW]'))
    CREATE INDEX [IX_sap_NBEW_ACCIDENT_NO] ON [dbo].[NBEW] ([ACCIDENT_NO]);
GO

/* ZNACCIDENT - טבלת האירועים (events / accidents) */
IF OBJECT_ID(N'[dbo].[ZNACCIDENT]', N'U') IS NULL
CREATE TABLE [dbo].[ZNACCIDENT]
(
    [ACCIDENT_NO]           NVARCHAR(12) NOT NULL,     -- UNFNR    מספר ארוע
    [ACCIDENT]              NVARCHAR(3)  NULL,         -- UNFKZ    סוג תאונה
    [MCI]                   NVARCHAR(1)  NULL,         -- ZNMCI    אר"ן
    [ACCIDENT_DATE]         DATE         NULL,         -- ZNUNFDT2 תאריך אירוע
    [ACCIDENT_TIME]         TIME(0)      NULL,         -- ZUNFZT2  שעת אירוע
    [ACCIDENT_PLACE]        NVARCHAR(100)      NULL,         -- ZUNFZT2  שעת אירוע
    [ACCIDENT_DESCRIPTION]  NVARCHAR(100)      NULL,         -- ZUNFZT2  שעת אירוע
    [END_DATE]              DATE         NULL,         -- ZNENDDATE תאריך סיום
    [END_TIME]              TIME(0)      NULL,         -- ZNENDTIME שעת סיום
    CONSTRAINT [PK_sap_ZNACCIDENT] PRIMARY KEY CLUSTERED ([ACCIDENT_NO])
);
GO

/* ZNMCICASE - טבלת מקרים באירוע (cases per event) */
IF OBJECT_ID(N'[dbo].[ZNMCICASE]', N'U') IS NULL
CREATE TABLE [dbo].[ZNMCICASE]
(
    [ACCIDENT_NO]    NVARCHAR(12) NOT NULL,    -- UNFNR        מספר קובץ / אירוע
    [PATCASEID]      NVARCHAR(10) NOT NULL,    -- FALNR        מספר מקרה
    [MAIN_INJURY]    NVARCHAR(2)  NULL,        -- ZNMAININJURY פציעה עיקרית
    [MEDICAL_STATUS] NVARCHAR(2)  NULL,        -- ZNMEDSTATUS  מצב רפואי
    CONSTRAINT [PK_sap_ZNMCICASE] PRIMARY KEY CLUSTERED ([ACCIDENT_NO], [PATCASEID])
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sap_ZNMCICASE_PATCASEID' AND object_id = OBJECT_ID(N'[dbo].[ZNMCICASE]'))
    CREATE INDEX [IX_sap_ZNMCICASE_PATCASEID] ON [dbo].[ZNMCICASE] ([PATCASEID]);
GO
