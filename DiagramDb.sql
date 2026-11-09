/*
 FlowStudio / практическая работа: SQL Server 2019
 Выполнить целиком в SSMS под учетной записью, имеющей право CREATE DATABASE.
*/
IF DB_ID(N'DiagramDb') IS NULL
    CREATE DATABASE DiagramDb;
GO
USE DiagramDb;
GO

IF OBJECT_ID(N'dbo.Shapes', N'U') IS NOT NULL DROP TABLE dbo.Shapes;
IF OBJECT_ID(N'dbo.ProjectHistory', N'U') IS NOT NULL DROP TABLE dbo.ProjectHistory;
IF OBJECT_ID(N'dbo.Projects', N'U') IS NOT NULL DROP TABLE dbo.Projects;
IF OBJECT_ID(N'dbo.ShapeTypes', N'U') IS NOT NULL DROP TABLE dbo.ShapeTypes;
GO

CREATE TABLE dbo.ShapeTypes
(
    ShapeTypeID INT IDENTITY(1,1) PRIMARY KEY,
    TypeName NVARCHAR(50) NOT NULL UNIQUE,
    DefaultWidth INT NOT NULL DEFAULT 80,
    DefaultHeight INT NOT NULL DEFAULT 50
);
INSERT INTO dbo.ShapeTypes(TypeName, DefaultWidth, DefaultHeight)
VALUES
(N'Rectangle', 140, 80),
(N'Ellipse', 140, 80),
(N'Rhombus', 140, 80),
(N'TextBlock', 140, 80),
(N'Terminator', 140, 80),
(N'ConnectionLine', 140, 80);
GO

CREATE TABLE dbo.Projects
(
    ProjectID INT IDENTITY(1,1) PRIMARY KEY,
    Title NVARCHAR(100) NOT NULL,
    Author NVARCHAR(50) NOT NULL DEFAULT N'Студент',
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CanvasWidth INT NOT NULL CHECK(CanvasWidth > 0),
    CanvasHeight INT NOT NULL CHECK(CanvasHeight > 0)
);
GO

CREATE TABLE dbo.Shapes
(
    ShapeID INT IDENTITY(1,1) PRIMARY KEY,
    ProjectID INT NOT NULL,
    ShapeTypeID INT NOT NULL,
    ShapeGuid UNIQUEIDENTIFIER NOT NULL,
    X INT NOT NULL,
    Y INT NOT NULL,
    Width INT NOT NULL CHECK(Width >= 10),
    Height INT NOT NULL CHECK(Height >= 10),
    StrokeColor NVARCHAR(9) NOT NULL DEFAULT N'#000000',
    FillColor NVARCHAR(9) NOT NULL DEFAULT N'#ADD8E6',
    StrokeWidth FLOAT NOT NULL DEFAULT 2.0 CHECK(StrokeWidth > 0),
    TextContent NVARCHAR(255) NULL,
    TextColor NVARCHAR(9) NOT NULL DEFAULT N'#000000',
    FontSize FLOAT NOT NULL DEFAULT 10.0,
    ZOrder INT NOT NULL DEFAULT 0,
    SourceShapeGuid UNIQUEIDENTIFIER NULL,
    TargetShapeGuid UNIQUEIDENTIFIER NULL,
    StartX INT NOT NULL DEFAULT 0,
    StartY INT NOT NULL DEFAULT 0,
    EndX INT NOT NULL DEFAULT 0,
    EndY INT NOT NULL DEFAULT 0,
    CONSTRAINT FK_Shapes_Projects FOREIGN KEY(ProjectID)
        REFERENCES dbo.Projects(ProjectID) ON DELETE CASCADE,
    CONSTRAINT FK_Shapes_ShapeTypes FOREIGN KEY(ShapeTypeID)
        REFERENCES dbo.ShapeTypes(ShapeTypeID),
    CONSTRAINT UQ_Shapes_Project_ShapeGuid UNIQUE(ProjectID, ShapeGuid)
);
GO

CREATE TABLE dbo.ProjectHistory
(
    HistoryID INT IDENTITY(1,1) PRIMARY KEY,
    ProjectID INT NOT NULL,
    ActionType NVARCHAR(20) NOT NULL,
    [Timestamp] DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    SnapshotCount INT NOT NULL,
    CONSTRAINT FK_ProjectHistory_Projects FOREIGN KEY(ProjectID)
        REFERENCES dbo.Projects(ProjectID) ON DELETE CASCADE
);
GO

CREATE OR ALTER PROCEDURE dbo.sp_SaveProject
    @ProjectID INT OUTPUT,
    @Title NVARCHAR(100),
    @Author NVARCHAR(50),
    @CanvasWidth INT,
    @CanvasHeight INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        IF @ProjectID IS NULL OR @ProjectID <= 0
           OR NOT EXISTS(SELECT 1 FROM dbo.Projects WHERE ProjectID = @ProjectID)
        BEGIN
            INSERT INTO dbo.Projects(Title, Author, CanvasWidth, CanvasHeight)
            VALUES(@Title, @Author, @CanvasWidth, @CanvasHeight);
            SET @ProjectID = CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            UPDATE dbo.Projects
            SET Title = @Title, Author = @Author,
                CanvasWidth = @CanvasWidth, CanvasHeight = @CanvasHeight,
                UpdatedAt = SYSDATETIME()
            WHERE ProjectID = @ProjectID;

            DELETE FROM dbo.Shapes WHERE ProjectID = @ProjectID;
        END
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetProjectShapes
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.ShapeID, s.ShapeGuid, s.X, s.Y, s.Width, s.Height,
           s.StrokeColor, s.FillColor, s.StrokeWidth, s.TextContent,
           s.TextColor, s.FontSize, s.ZOrder, s.SourceShapeGuid,
           s.TargetShapeGuid, s.StartX, s.StartY, s.EndX, s.EndY,
           st.TypeName
    FROM dbo.Shapes s
    INNER JOIN dbo.ShapeTypes st ON st.ShapeTypeID = s.ShapeTypeID
    WHERE s.ProjectID = @ProjectID
    ORDER BY s.ZOrder ASC;
END;
GO

CREATE OR ALTER VIEW dbo.v_ProjectStatistics
AS
SELECT
    p.ProjectID AS [ID],
    p.Title AS [Название схемы],
    p.Author AS [Автор],
    p.UpdatedAt AS [Последнее изменение],
    COUNT(s.ShapeID) AS [Всего фигур],
    ISNULL(SUM(CASE WHEN st.TypeName = N'Rhombus' THEN 1 ELSE 0 END), 0) AS [Блоков условий],
    ISNULL(SUM(CASE WHEN st.TypeName = N'TextBlock' THEN 1 ELSE 0 END), 0) AS [Текстовых заметок]
FROM dbo.Projects p
LEFT JOIN dbo.Shapes s ON s.ProjectID = p.ProjectID
LEFT JOIN dbo.ShapeTypes st ON st.ShapeTypeID = s.ShapeTypeID
GROUP BY p.ProjectID, p.Title, p.Author, p.UpdatedAt;
GO

CREATE OR ALTER TRIGGER dbo.trg_Projects_Audit
ON dbo.Projects
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.ProjectHistory(ProjectID, ActionType, SnapshotCount)
    SELECT i.ProjectID,
           CASE WHEN EXISTS(SELECT 1 FROM deleted) THEN N'UPDATE' ELSE N'INSERT' END,
           (SELECT COUNT(*) FROM dbo.Shapes s WHERE s.ProjectID = i.ProjectID)
    FROM inserted i;
END;
GO

-- Проверка созданных объектов
SELECT name, type_desc
FROM sys.objects
WHERE name IN (N'ShapeTypes', N'Projects', N'Shapes', N'ProjectHistory',
               N'sp_SaveProject', N'sp_GetProjectShapes',
               N'v_ProjectStatistics', N'trg_Projects_Audit')
ORDER BY type_desc, name;
GO
