-- Customer KYC POC — SQL Server schema
-- Run against CustomerKycDb (created by docker-compose init or manually)

IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'CustomerKycDb')
BEGIN
    CREATE DATABASE CustomerKycDb;
END
GO

USE CustomerKycDb;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'CustomerKyc'
)
BEGIN
    CREATE TABLE dbo.CustomerKyc
    (
        Id                BIGINT         IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CustomerReference NVARCHAR(100)  NOT NULL,
        FirstName         NVARCHAR(100)  NOT NULL,
        LastName          NVARCHAR(100)  NOT NULL,
        EncryptedPan      NVARCHAR(MAX)  NOT NULL,
        EncryptedAadhaar  NVARCHAR(MAX)  NOT NULL,
        Status            NVARCHAR(50)   NOT NULL DEFAULT 'Active',
        CreatedOn         DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_CustomerKyc_CustomerReference
        ON dbo.CustomerKyc (CustomerReference);

    PRINT 'Table CustomerKyc created.';
END
ELSE
BEGIN
    PRINT 'Table CustomerKyc already exists.';
END
GO
