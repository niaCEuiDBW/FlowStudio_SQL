using System.Data;
using System.Drawing;
using FlowStudio.Models;
using Microsoft.Data.SqlClient;

namespace FlowStudio.Services;

/// <summary>
/// Единственная точка доступа FlowStudio к Microsoft SQL Server.
/// SQL-запросы не размещаются в формах.
/// </summary>
public sealed class DatabaseService
{
    // При необходимости замените localhost на .\SQLEXPRESS.
    private readonly string _connectionString;

    public DatabaseService(string? connectionString = null)
    {
        _connectionString = connectionString ??
            "Server=localhost;Database=DiagramDb;Integrated Security=True;TrustServerCertificate=True;";
    }

    public async Task<bool> TestConnectionAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync();
        return true;
    }

    public async Task<DataTable> GetProjectsSummaryAsync(string? author = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        const string sql = @"
SELECT [ID], [Название схемы], [Автор], [Последнее изменение],
       [Всего фигур], [Блоков условий], [Текстовых заметок]
FROM v_ProjectStatistics
WHERE (@Author IS NULL OR [Автор] LIKE N'%' + @Author + N'%')
  AND (@DateFrom IS NULL OR CONVERT(date, [Последнее изменение]) >= @DateFrom)
  AND (@DateTo IS NULL OR CONVERT(date, [Последнее изменение]) <= @DateTo)
ORDER BY [Последнее изменение] DESC;";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Author", SqlDbType.NVarChar, 50).Value =
            string.IsNullOrWhiteSpace(author) ? DBNull.Value : author.Trim();
        command.Parameters.Add("@DateFrom", SqlDbType.Date).Value = dateFrom.HasValue ? dateFrom.Value.Date : DBNull.Value;
        command.Parameters.Add("@DateTo", SqlDbType.Date).Value = dateTo.HasValue ? dateTo.Value.Date : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    public async Task<int> SaveProjectAsync(int? projectId, string title, IReadOnlyList<Shape> shapes, int canvasWidth, int canvasHeight)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            await using (var projectCommand = new SqlCommand("sp_SaveProject", connection, transaction))
            {
                projectCommand.CommandType = CommandType.StoredProcedure;
                var id = new SqlParameter("@ProjectID", SqlDbType.Int)
                {
                    Direction = ParameterDirection.InputOutput,
                    Value = (object?)projectId ?? DBNull.Value
                };
                projectCommand.Parameters.Add(id);
                projectCommand.Parameters.Add("@Title", SqlDbType.NVarChar, 100).Value = title;
                projectCommand.Parameters.Add("@Author", SqlDbType.NVarChar, 50).Value =
                    string.IsNullOrWhiteSpace(Environment.UserName) ? "Студент" : Environment.UserName;
                projectCommand.Parameters.Add("@CanvasWidth", SqlDbType.Int).Value = Math.Max(1, canvasWidth);
                projectCommand.Parameters.Add("@CanvasHeight", SqlDbType.Int).Value = Math.Max(1, canvasHeight);
                await projectCommand.ExecuteNonQueryAsync();
                projectId = Convert.ToInt32(id.Value);
            }

            // Процедура обновления очищает прежние фигуры. При новой записи таблица пуста.
            const string insertSql = @"
INSERT INTO Shapes
(ProjectID, ShapeTypeID, ShapeGuid, X, Y, Width, Height, StrokeColor, FillColor,
 StrokeWidth, TextContent, TextColor, FontSize, ZOrder, SourceShapeGuid, TargetShapeGuid,
 StartX, StartY, EndX, EndY)
SELECT @ProjectID, st.ShapeTypeID, @ShapeGuid, @X, @Y, @Width, @Height, @StrokeColor, @FillColor,
       @StrokeWidth, @TextContent, @TextColor, @FontSize, @ZOrder, @SourceShapeGuid, @TargetShapeGuid,
       @StartX, @StartY, @EndX, @EndY
FROM ShapeTypes st WHERE st.TypeName = @TypeName;";

            int order = 0;
            foreach (Shape shape in shapes)
            {
                string typeName = shape switch
                {
                    RectangleShape => "Rectangle",
                    EllipseShape => "Ellipse",
                    RhombusShape => "Rhombus",
                    TextBlockShape => "TextBlock",
                    TerminatorShape => "Terminator",
                    ConnectionLineShape => "ConnectionLine",
                    _ => throw new InvalidOperationException($"Неизвестный тип фигуры: {shape.GetType().Name}")
                };

                await using var command = new SqlCommand(insertSql, connection, transaction);
                command.Parameters.Add("@ProjectID", SqlDbType.Int).Value = projectId.Value;
                command.Parameters.Add("@TypeName", SqlDbType.NVarChar, 50).Value = typeName;
                command.Parameters.Add("@ShapeGuid", SqlDbType.UniqueIdentifier).Value = shape.Id;
                command.Parameters.Add("@X", SqlDbType.Int).Value = shape.X;
                command.Parameters.Add("@Y", SqlDbType.Int).Value = shape.Y;
                command.Parameters.Add("@Width", SqlDbType.Int).Value = Math.Max(10, shape.Width);
                command.Parameters.Add("@Height", SqlDbType.Int).Value = Math.Max(10, shape.Height);
                command.Parameters.Add("@StrokeColor", SqlDbType.NVarChar, 9).Value = ToHex(shape.StrokeColor);
                command.Parameters.Add("@FillColor", SqlDbType.NVarChar, 9).Value = ToHex(shape.FillColor);
                command.Parameters.Add("@StrokeWidth", SqlDbType.Float).Value = shape.StrokeWidth;
                command.Parameters.Add("@TextContent", SqlDbType.NVarChar, 255).Value =
                    shape.Text.Length > 255 ? shape.Text[..255] : shape.Text;
                command.Parameters.Add("@TextColor", SqlDbType.NVarChar, 9).Value = ToHex(shape.TextColor);
                command.Parameters.Add("@FontSize", SqlDbType.Float).Value = shape.FontSize;
                command.Parameters.Add("@ZOrder", SqlDbType.Int).Value = order++;
                var line = shape as ConnectionLineShape;
                command.Parameters.Add("@SourceShapeGuid", SqlDbType.UniqueIdentifier).Value = (object?)line?.SourceShapeId ?? DBNull.Value;
                command.Parameters.Add("@TargetShapeGuid", SqlDbType.UniqueIdentifier).Value = (object?)line?.TargetShapeId ?? DBNull.Value;
                command.Parameters.Add("@StartX", SqlDbType.Int).Value = line?.StartPoint.X ?? 0;
                command.Parameters.Add("@StartY", SqlDbType.Int).Value = line?.StartPoint.Y ?? 0;
                command.Parameters.Add("@EndX", SqlDbType.Int).Value = line?.EndPoint.X ?? 0;
                command.Parameters.Add("@EndY", SqlDbType.Int).Value = line?.EndPoint.Y ?? 0;

                int affected = await command.ExecuteNonQueryAsync();
                if (affected == 0)
                    throw new InvalidOperationException($"Тип фигуры {typeName} отсутствует в справочнике ShapeTypes.");
            }

            // Триггер срабатывает при создании/обновлении метаданных проекта,
            // поэтому после вставки фигур уточняем количество объектов в снимке аудита.
            await using (var auditCommand = new SqlCommand(@"
UPDATE TOP (1) dbo.ProjectHistory
SET SnapshotCount = (SELECT COUNT(*) FROM dbo.Shapes WHERE ProjectID = @ProjectID)
WHERE ProjectID = @ProjectID
  AND HistoryID = (SELECT MAX(HistoryID) FROM dbo.ProjectHistory WHERE ProjectID = @ProjectID);",
                connection, transaction))
            {
                auditCommand.Parameters.Add("@ProjectID", SqlDbType.Int).Value = projectId.Value;
                await auditCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return projectId.Value;
        }
        catch
        {
            try { await transaction.RollbackAsync(); } catch { /* Процедура могла уже откатить транзакцию. */ }
            throw;
        }
    }

    public async Task<(List<Shape> Shapes, int Width, int Height, string Title)> LoadProjectAsync(int projectId)
    {
        var shapes = new List<Shape>();
        int canvasWidth = 1100, canvasHeight = 700;
        string title = $"Проект {projectId}";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using (var projectCommand = new SqlCommand(
            "SELECT Title, CanvasWidth, CanvasHeight FROM Projects WHERE ProjectID = @ProjectID", connection))
        {
            projectCommand.Parameters.Add("@ProjectID", SqlDbType.Int).Value = projectId;
            await using var projectReader = await projectCommand.ExecuteReaderAsync();
            if (!await projectReader.ReadAsync())
                throw new InvalidOperationException("Проект не найден.");
            title = projectReader.GetString(0);
            canvasWidth = projectReader.GetInt32(1);
            canvasHeight = projectReader.GetInt32(2);
        }

        await using var command = new SqlCommand("sp_GetProjectShapes", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@ProjectID", SqlDbType.Int).Value = projectId;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string type = reader.GetString(reader.GetOrdinal("TypeName"));
            Shape shape = type switch
            {
                "Rectangle" => new RectangleShape(),
                "Ellipse" => new EllipseShape(),
                "Rhombus" => new RhombusShape(),
                "TextBlock" => new TextBlockShape(),
                "Terminator" => new TerminatorShape(),
                "ConnectionLine" => new ConnectionLineShape(),
                _ => throw new InvalidOperationException($"Неизвестный тип фигуры в БД: {type}")
            };

            shape.Id = reader.GetGuid(reader.GetOrdinal("ShapeGuid"));
            shape.X = reader.GetInt32(reader.GetOrdinal("X"));
            shape.Y = reader.GetInt32(reader.GetOrdinal("Y"));
            shape.Width = reader.GetInt32(reader.GetOrdinal("Width"));
            shape.Height = reader.GetInt32(reader.GetOrdinal("Height"));
            shape.StrokeColor = ColorTranslator.FromHtml(reader.GetString(reader.GetOrdinal("StrokeColor")));
            shape.FillColor = ColorTranslator.FromHtml(reader.GetString(reader.GetOrdinal("FillColor")));
            shape.StrokeWidth = Convert.ToSingle(reader.GetValue(reader.GetOrdinal("StrokeWidth")));
            shape.Text = reader.IsDBNull(reader.GetOrdinal("TextContent")) ? "" : reader.GetString(reader.GetOrdinal("TextContent"));
            shape.TextColor = ColorTranslator.FromHtml(reader.GetString(reader.GetOrdinal("TextColor")));
            shape.FontSize = Convert.ToSingle(reader.GetValue(reader.GetOrdinal("FontSize")));

            if (shape is ConnectionLineShape connectionLine)
            {
                int sourceOrdinal = reader.GetOrdinal("SourceShapeGuid");
                int targetOrdinal = reader.GetOrdinal("TargetShapeGuid");
                if (!reader.IsDBNull(sourceOrdinal)) connectionLine.SourceShapeId = reader.GetGuid(sourceOrdinal);
                if (!reader.IsDBNull(targetOrdinal)) connectionLine.TargetShapeId = reader.GetGuid(targetOrdinal);
                connectionLine.StartPoint = new Point(reader.GetInt32(reader.GetOrdinal("StartX")), reader.GetInt32(reader.GetOrdinal("StartY")));
                connectionLine.EndPoint = new Point(reader.GetInt32(reader.GetOrdinal("EndX")), reader.GetInt32(reader.GetOrdinal("EndY")));
            }
            shape.IsSelected = false;
            shapes.Add(shape);
        }

        return (shapes, canvasWidth, canvasHeight, title);
    }

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public async Task DeleteProjectAsync(int projectId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("DELETE FROM Projects WHERE ProjectID = @ProjectID", connection);
        command.Parameters.Add("@ProjectID", SqlDbType.Int).Value = projectId;
        if (await command.ExecuteNonQueryAsync() == 0)
            throw new InvalidOperationException("Проект не найден или уже удалён.");
    }
}
