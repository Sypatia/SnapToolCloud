using iText.Commons.Utils;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Exceptions;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using SnapToolCloud.Data;
using SnapToolCloud.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

namespace SnapTool.PdfGenerator
{
    internal class Definitions
    {
        // Locations of relevant piles and dolphins on PDF P-0504300-PPT-SK-001 - PPT Wharf Plans.pdf, noting reference is top-left corner of the file
        // Units are in centimetres
        public static Dictionary<string, (float X, float Y, int Page)> PileDolphinPdfLocations { get; private set; }

        public static Dictionary<string, (float X, float Y, float Width, float Height, int Page)> GridPdfLocations { get; private set; }

        // Static constructor to initialize the dictionary
        static Definitions()
        {
            PileDolphinPdfLocations = new Dictionary<string, (float X, float Y, int Page)>();
            GridPdfLocations = new Dictionary<string, (float X, float Y, float Width, float Height, int Page)>();
            try
            {
                PopulatePileDolphinLocations();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PileDolphin initialization failed:\n{ex}");
            }

            try
            {
                PopulateGridLocations();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Grid initialization failed:\n{ex}");
            }
        }

        private static void PopulatePileDolphinLocations()
        {
            // Load the embedded CSV resource
            string csvContent = Encoding.UTF8.GetString(Resources.PileDolphinPdfLocations);

            // Parse the CSV data into a list of LocationRecord objects
            var locations = CsvDataLoader.ParseRecords<PileDolphinLocationRecord>(csvContent);

            // Populate the dictionary with parsed data
            foreach (var location in locations)
            {
                PileDolphinPdfLocations[location.Name] = (location.X, location.Y, location.Page);
            }

        }

        private static void PopulateGridLocations()
        {
            // Load the embedded CSV resource
            string csvContent = Encoding.UTF8.GetString(Resources.GridPdfLocations);

            // Parse the CSV data into a list of LocationRecord objects
            var locations = CsvDataLoader.ParseRecords<GridLocationRecord>(csvContent);

            // Populate the dictionary with parsed data
            foreach (var location in locations)
            {

                if ((location.Width == null || location.Height == null))
                    throw new ArgumentNullException("Null has occurred.. Should never be here.");

                GridPdfLocations[location.Name] = (location.X, location.Y, (float)location.Width, (float)location.Height, location.Page);
            }

        }
    }

    public class WharfTxtGenerator
    {
        public static void GeneratePileDolphinsTxtFile(string filePath, List<PileDolphinRecord> activePileDolphins)
        {
            // Use a StringBuilder to construct the file content
            var sb = new StringBuilder();

            // Define column headers and their widths
            string header = string.Format(
                "{0,-20} {1,-13} {2,-10} {3,-6} {4,-15} {5,-12} {6,-10} {7,-10} {8,-20} {9,-15}",
                "WeatherDateTime", "Berth", "Vessel", "MC", "ML", "Active", "Type", "Tension", "Location", "Cause"
            );
            sb.AppendLine(header);
            sb.AppendLine(new string('-', header.Length)); // Add a separator line

            // Add each record
            foreach (var record in activePileDolphins)
            {
                sb.AppendLine(string.Format(
                    "{0,-20} {1,-13} {2,-10} {3,-6} {4,-18} {5,-9} {6,-12} {7,-12} {8,-17} {9,-15}",
                    record.WeatherDateTime ?? "N/A",
                    record.Berth ?? "N/A",
                    record.Vessel ?? "N/A",
                    record.MC ?? "N/A",
                    record.ML ?? "N/A",
                    record.Active ?? "N/A",
                    record.Type ?? "N/A",
                    (record.TriggeringTension?.ToString("0.#") ?? "N/A"),
                    record.CombinedLocation ?? "N/A",
                    record.Cause ?? "N/A" // Add Cause here
                ));
            }

            // Write the content to the file
            File.WriteAllText(filePath, sb.ToString());
        }
    }
    public class WharfPlanPdfGenerator
    {
        public static void GenerateSnapbackPdf(
    string outputPdfPath,
    List<PileDolphinRecord> activePileDolphins,
    List<DockingArrangementRecord> arrangements,
    List<DateTime> timeSlots,
    List<WeatherRecord> weatherForecast,
    List<GridRecord> activeGrids = null)
        {
            // Get a distinct list of active piles/dolphins by their unique location
            var distinctActivePileDolphins = activePileDolphins
                .GroupBy(record => record.CombinedLocation) // Group by unique Location
                .Select(group => group.First())     // Take the first record from each group
                .ToList();

            // Get the names of distinct active piles/dolphins
            var activePileDolphinNames = new HashSet<string>(
                distinctActivePileDolphins.Select(record => record.CombinedLocation),
                StringComparer.OrdinalIgnoreCase);



            var pileDolphinLocations = Definitions.PileDolphinPdfLocations;
            var gridLocations = Definitions.GridPdfLocations;
            string tempFile1 = System.IO.Path.GetTempFileName();
            string tempFile2 = System.IO.Path.GetTempFileName();
            string tempFile3 = System.IO.Path.GetTempFileName();
            if (File.Exists(outputPdfPath))
            {
                try
                {
                    using (var stream = File.OpenWrite(outputPdfPath))
                    {
                        // File is not locked
                    }
                }
                catch (IOException)
                {
                    throw new IOException($"The file {outputPdfPath} is locked by another process.");
                }
            }

            try
            {
                // Open the source PDF
                using (var resourceStream = new MemoryStream(Resources.WharfPlan))
                using (var pdfReader = new PdfReader(resourceStream))
                using (var pdfWriter = new PdfWriter(tempFile1))
                using (var pdfDocument = new PdfDocument(pdfReader, pdfWriter))
                {
                    if (pdfReader.IsEncrypted())
                    {
                        throw new InvalidOperationException("The PDF is encrypted and cannot be processed without a password.");
                    }
                    float xCm = 0f, yCm = 0f;
                    int pageIndex = 0;
                    foreach (var location in pileDolphinLocations)
                    {
                        string name = location.Key;
                        (xCm, yCm, pageIndex) = location.Value;

                        // Get the page dimensions
                        var pageSize = pdfDocument.GetPage(pageIndex).GetPageSize();
                        float pageHeight = pageSize.GetHeight();
                        float pageWidth = pageSize.GetWidth();

                        // Swap X and Y coordinates
                        // iText PDF generator uses a coordinate system top left as (0,0) with Y increasing to the right and X increasing down
                        float adjustedXPoints = pageWidth - (yCm / 2.54f) * 72f;                    // flip X
                        float adjustedYPoints = pageHeight - (xCm / 2.54f) * 72f;       // flip Y

                        // Determine the colour: RED for active, GREEN otherwise
                        System.Drawing.Color circleColour = activePileDolphinNames.Contains(name) ? System.Drawing.Color.Red : System.Drawing.Color.Green;

                        // Add a circle to the specified page
                        AddCircleToPage(pdfDocument, pageIndex, adjustedXPoints, adjustedYPoints, radius: 0.5f, colour: circleColour);

                    }

                    if (activeGrids != null)
                    {
                        var distinctActiveGrids = activeGrids
                            .GroupBy(record => record.CombinedLocation) // Group by unique Location
                            .Select(group => group.First())     // Take the first record from each group
                            .ToList();

                        // Get the names of distinct active grids
                        var activeGridNames = new HashSet<string>(
                            distinctActiveGrids.Select(record => record.CombinedLocation),
                            StringComparer.OrdinalIgnoreCase);

                        foreach (var location in gridLocations)
                        {
                            string name = location.Key;
                            float widthCm, heightCm;

                            (xCm, yCm, widthCm, heightCm, pageIndex) = location.Value;

                            var page = pdfDocument.GetPage(pageIndex);
                            var pageSize = page.GetPageSize();
                            float pageHeight = pageSize.GetHeight();
                            float pageWidth = pageSize.GetWidth();

                            // Convert from cm to PDF points and adjust orientation
                            float adjustedXPoints = pageWidth - ((yCm + heightCm) / 2.54f) * 72f;                    // normal
                            float adjustedYPoints = pageHeight - ((xCm + widthCm) / 2.54f) * 72f;       // flip Y
                            float widthPoints = (heightCm / 2.54f) * 72f;
                            float heightPoints = (widthCm / 2.54f) * 72f;

                            // Determine colour: RED for active, GREEN otherwise
                            System.Drawing.Color rectColour = activeGridNames.Contains(name) ? System.Drawing.Color.Red : System.Drawing.Color.Green;

                            // Draw translucent rectangle
                            AddTranslucentRectangleToPage(
                                pdfDocument,
                                pageIndex,
                                adjustedXPoints,
                                adjustedYPoints,
                                widthPoints,
                                heightPoints,
                                rectColour,
                                opacity: 0.3f
                            );
                        }
                    }

                    AddSelectedVesselsToPage(pdfDocument, pageIndex, arrangements, timeSlots);

                }

                var selectedWeatherData = weatherForecast
                    .Where(x => timeSlots.Contains(x.DateTime))
                    .ToList();

                // Open the source PDF
                using (var pdfReader = new PdfReader(tempFile1))
                using (var pdfWriter = new PdfWriter(tempFile2))
                using (var pdfDocument = new PdfDocument(pdfReader, pdfWriter))
                {
                    if (pdfReader.IsEncrypted())
                    {
                        throw new InvalidOperationException("The PDF is encrypted and cannot be processed without a password.");
                    }
                    var pageIndex = 1;
                    AddWeatherForecastTableToPage(selectedWeatherData, pdfDocument, pageIndex);

                }

                // Open the source PDF
                using (var pdfReader = new PdfReader(tempFile2))
                using (var pdfWriter = new PdfWriter(outputPdfPath))
                using (var pdfDocument = new PdfDocument(pdfReader, pdfWriter))
                {
                    if (pdfReader.IsEncrypted())
                    {
                        throw new InvalidOperationException("The PDF is encrypted and cannot be processed without a password.");
                    }
                    var pageIndex = 1;
                    AddDolphinTableToPagePdfDocument(pdfDocument, pageIndex, arrangements, timeSlots, weatherForecast);

                }

                // Cleanup temp file
                if (File.Exists(tempFile1))
                {
                    File.Delete(tempFile1);
                }
                // Cleanup temp file
                if (File.Exists(tempFile2))
                {
                    File.Delete(tempFile2);
                }
                // Cleanup temp file
                if (File.Exists(tempFile3))
                {
                    File.Delete(tempFile3);
                }
            }
            catch (PdfException ex)
            {
                Console.WriteLine($"PdfException occurred: {ex.Message}");
            }

            Console.WriteLine($"Annotated PDF generated: {outputPdfPath}");
        }

        private static void AddTranslucentRectangleToPage(
        PdfDocument pdfDocument,
        int pageIndex,
        float x,
        float y,
        float width,
        float height,
        System.Drawing.Color colour,
        float opacity = 0.3f)
        {
            var page = pdfDocument.GetPage(pageIndex);
            var canvas = new PdfCanvas(page);

            var iTextColor = new DeviceRgb(colour.R, colour.G, colour.B);

            // Set transparency
            var gs = new PdfExtGState().SetFillOpacity(opacity);
            canvas.SetExtGState(gs);


            // Draw rectangle
            canvas.SetFillColor(iTextColor);
            canvas.Rectangle(x, y, width, height);
            canvas.Fill();
        }

        // float x and y represent CENTRE of circle
        private static void AddCircleToPage(PdfDocument pdfDocument, int pageIndex, float x, float y, float radius, System.Drawing.Color colour)
        {
            var page = pdfDocument.GetPage(pageIndex);
            var canvas = new PdfCanvas(page);

            var iTextColor = new DeviceRgb(colour.R, colour.G, colour.B);

            // Offset the circle's position by the radius
            float adjustedX = x + radius / 2;
            float adjustedY = y + radius / 2;

            // Draw a circle
            canvas.SetLineWidth(1);
            canvas.SetStrokeColor(iTextColor);
            canvas.SetFillColor(iTextColor);
            canvas.Circle(adjustedX, adjustedY, radius);
            canvas.FillStroke();
        }

        private static void AddSelectedVesselsToPage(PdfDocument pdfDocument, int pageIndex, List<DockingArrangementRecord> arrangements, List<DateTime> timeSlots)
        {
            var page = pdfDocument.GetPage(pageIndex);
            var canvas = new PdfCanvas(page);

            var blue = System.Drawing.Color.Blue;
            var iTextColor = new DeviceRgb(blue.R, blue.G, blue.B); // Blue text
                                                                    // Get the page dimensions
            var pageSize = pdfDocument.GetPage(pageIndex).GetPageSize();
            float pageHeight = pageSize.GetHeight();
            float pageWidth = pageSize.GetWidth();
            // Starting coordinates for the text
            float startX = pageWidth - 50;
            float startY = 20;
            float lineHeight = 4;  // Height between lines of text

            // Rotate text anticlockwise 90 degrees by modifying SetTextMatrix
            canvas.BeginText();
            canvas.SetFontAndSize(PdfFontFactory.CreateFont(), 3);
            canvas.SetColor(iTextColor, true);  // true parameter means it's for filling text
            // Set text matrix to rotate the text anticlockwise 90 degrees
            // First two parameters: startX, startY (position)
            // Third and fourth parameters: rotation matrix for text (0, 1, -1 for anticlockwise 90 degrees)
            // The rotation is applied about the point (startX, startY)
            canvas.SetTextMatrix(0, -1, 1, 0, startX, pageHeight - startY);


            // Initial text position
            canvas.ShowText("Weather Forecasts:");
            canvas.MoveText(0, -2 * lineHeight);  // Move down two lines

            // Iterate over the time slots
            foreach (var timeSlot in timeSlots)
            {
                canvas.ShowText($"{timeSlot}");
                canvas.MoveText(0, -lineHeight);  // Move down for next line
            }

            // Add extra space between sections
            canvas.MoveText(0, -lineHeight * 2);  // Move down two lines

            canvas.ShowText("Berth Setups:");
            canvas.MoveText(0, -2 * lineHeight);  // Move down two lines

            foreach (var arrangement in arrangements)
            {
                var text = $"{arrangement.Berth} with {arrangement.Vessel}";

                if (!string.IsNullOrEmpty(arrangement.VesselName))
                    text += $" '{arrangement.VesselName}'";

                text += $" using {arrangement.MC}";

                var ptbInfo = !string.IsNullOrEmpty(arrangement.PlanToBerth.ToString()) ? $" | PTB: {arrangement.PlanToBerth.ToString()}" : "";
                var ptsInfo = !string.IsNullOrEmpty(arrangement.PlanToSail.ToString()) ? $" | PTS: {arrangement.PlanToSail.ToString()}" : "";

                canvas.ShowText(text + ptbInfo + ptsInfo);
                canvas.MoveText(0, -lineHeight);
            }


            canvas.EndText();
        }

        private static void AddWeatherForecastTableToPage(List<WeatherRecord> selectedWeatherData, PdfDocument pdfDocument, int pageIndex)
        {
            var numCols = 9;
            var numRows = selectedWeatherData.Count;

            float[] columnWidths = new float[] { 1f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f };
            Table table = new Table(UnitValue.CreatePercentArray(columnWidths));

            // Define base fonts
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            float fontSize = 4f; // reduced size

            var columnNames = typeof(WeatherRecord)
                .GetProperties()
                .Select(p => p.Name)
                .ToList();
            // Set headers
            for (int j = 0; j < numCols; j++)
            {
                string columnName = columnNames[j];
                table.AddHeaderCell(
                    new Cell().Add(new Paragraph(columnName)
                        .SetFont(boldFont)
                        .SetFontSize(fontSize)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetPadding(0)
                        .SetMargin(0))
                );
            }

            var records = selectedWeatherData;
            var properties = typeof(WeatherRecord).GetProperties();  // or nameof(your record type)
            numRows = records.Count;
            numCols = properties.Length;

            for (int i = 0; i < numRows; i++)
            {
                var record = records[i];
                for (int j = 0; j < numCols; j++)
                {
                    var value = properties[j].GetValue(record);
                    string cellValue = value?.ToString() ?? "-";
                    if (string.IsNullOrWhiteSpace(cellValue))
                        cellValue = "-";

                    table.AddCell(
                        new Cell().Add(new Paragraph(cellValue)
                            .SetFont(font)
                            .SetFontSize(fontSize)
                            .SetTextAlignment(TextAlignment.CENTER))
                    );
                }
            }

            // Get the page and its dimensions
            PdfPage page = pdfDocument.GetPage(pageIndex);
            Rectangle pageSize = page.GetPageSize();
            float pageWidth = pageSize.GetWidth();
            float pageHeight = pageSize.GetHeight();

            // ---- Placement ----
            float margin = 50;

            // pivot closer to right edge but keep table visible
            float startX = pageWidth - margin * 8;
            float startY = pageHeight - margin;

            // Apply rotation (90° clockwise, upright on right edge)
            PdfCanvas pdfCanvas = new PdfCanvas(page);
            pdfCanvas.ConcatMatrix(0, -1, 1, 0, startX, startY);

            // Create a canvas for the rotated system
            Canvas canvas = new Canvas(pdfCanvas, pageSize);

            // ↓ ensure the table fits after smaller font
            table.SetFontSize(4);
            table.SetWidth(300);     // shrink width to avoid overflow
            table.SetFixedPosition(0, 0, 300); // anchor inside the rotated space
            table.SetMargin(0);
            table.SetPadding(0);

            foreach (var row in table.GetChildren())
            {
                if (row is iText.Layout.Element.Cell cell)
                {
                    cell
                        .SetPadding(0)
                        .SetMargin(0)
                        .SetTextAlignment(TextAlignment.CENTER);
                }
                else if (row is iText.Layout.Element.IBlockElement block && block is iText.Layout.Element.Table innerTable)
                {
                    // Optional: recursively handle nested tables if you ever use them
                    foreach (var innerCell in innerTable.GetChildren().OfType<Cell>())
                    {
                        innerCell
                            .SetPadding(0)
                            .SetMargin(0)
                            .SetTextAlignment(TextAlignment.CENTER);
                    }
                }
            }

            canvas.Add(table);
            canvas.Close();

        }


        private static void AddDolphinTableToPagePdfDocument(PdfDocument pdfDocument, int pageIndex, List<DockingArrangementRecord> arrangements, List<DateTime> timeSlots, List<WeatherRecord> weatherForecast)
        {
            var mooringData = DolphinMooringLoader.ParseRecords();

            // Define custom ML sorting order
            var mlOrder = new List<string> { "Bow", "Fwd Spring", "Aft Spring", "Stern" };

            // Get all dolphins
            var dolphins = mooringData
                .Where(d =>
                    arrangements.Any(a =>
                        a.Berth == "Berth 0" + d.Berth &&
                        a.Vessel == d.Vessel &&
                        a.MC == d.MC))
                .OrderBy(d => d.Berth)
                .ThenBy(d => d.Vessel)
                .ThenBy(d => d.MC)
                .ToList();




            List<PileDolphinRecord> activePileDolphins = new List<PileDolphinRecord>();
            foreach (var timeSlot in timeSlots)
            {

                foreach (var arrangement in arrangements)
                {
                    var filteredWindRecords = DataFilter.FilterDMADataWindOnly(
                        dockingArrangement: arrangement,
                        targetDateTime: timeSlot,
                        weatherForecast: weatherForecast,
                        filterTension: false);

                    var filteredWaveRecords = DataFilter.FilterDMADataWaveOnly(
                        dockingArrangement: arrangement,
                        targetDateTime: timeSlot,
                        weatherForecast: weatherForecast,
                        filterTension: false);

                    activePileDolphins.AddRange(DataFilter.FilterActiveRecords(filteredWindRecords, PileDolphinDataLoader.ParseRecords));
                    activePileDolphins.AddRange(DataFilter.FilterActiveRecords(filteredWaveRecords, PileDolphinDataLoader.ParseRecords));
                }
            }

            // Get maximum tension per dolphin location
            var maxTensionByLocation = activePileDolphins
                .GroupBy(d => d.CombinedLocation)
                .ToDictionary(g => g.Key, g => g.Max(d => d.TriggeringTension));

            float[] columnWidths = new float[] { 1f, 1, 1, 1, 1, 1, 1};
            Table table = new Table(UnitValue.CreatePercentArray(columnWidths));

            string[] headers = { "Berth", "Vessel", "MC", "Bow", "Fwd Spring", "Aft Spring", "Stern" };
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            float fontSize = 4f; // reduced size
            foreach (var header in headers)
            {
                table.AddHeaderCell(new Cell().Add(new Paragraph(header))
                    .SetTextAlignment(TextAlignment.CENTER))
                    .SetFont(boldFont)
                    .SetFontSize(fontSize);
            }

            foreach (var dolphin in dolphins)
            {

                table.AddCell(new Cell().Add(new Paragraph(dolphin.Berth)).SetTextAlignment(TextAlignment.CENTER).SetFontSize(fontSize));
                table.AddCell(new Cell().Add(new Paragraph(dolphin.Vessel)).SetTextAlignment(TextAlignment.CENTER).SetFontSize(fontSize));
                table.AddCell(new Cell().Add(new Paragraph(dolphin.MC)).SetTextAlignment(TextAlignment.CENTER).SetFontSize(fontSize));
                table.AddCell(new Cell().Add(new Paragraph(dolphin.Bow)).SetTextAlignment(TextAlignment.CENTER).SetFontSize(fontSize));
                table.AddCell(new Cell().Add(new Paragraph(dolphin.FwdSpring)).SetTextAlignment(TextAlignment.CENTER).SetFontSize(fontSize));
                table.AddCell(new Cell().Add(new Paragraph(dolphin.AftSpring)).SetTextAlignment(TextAlignment.CENTER).SetFontSize(fontSize));
                table.AddCell(new Cell().Add(new Paragraph(dolphin.Stern)).SetTextAlignment(TextAlignment.CENTER).SetFontSize(fontSize));
            }

            // Validate page index (iText uses 1-based indexing)
            if (pageIndex < 1 || pageIndex > pdfDocument.GetNumberOfPages())
                throw new ArgumentOutOfRangeException(nameof(pageIndex),
                    $"Invalid page index {pageIndex}. Document has {pdfDocument.GetNumberOfPages()} pages.");

            // Validate table
            if (table == null)
                throw new ArgumentNullException(nameof(table), "Table object is null.");

            // Retrieve page
            PdfPage page = pdfDocument.GetPage(pageIndex);
            if (page == null)
                throw new NullReferenceException($"Page {pageIndex} could not be retrieved from PDF document.");

            Rectangle pageSize = page.GetPageSize();
            float pageWidth = pageSize.GetWidth();
            float pageHeight = pageSize.GetHeight();

            // ---- Placement ----
            float startX = pageWidth / 2 - 102.5f;  // center X pivot
            float startY = pageHeight / 2; // center Y pivot

            // ---- Rotate 90° counterclockwise ----
            PdfCanvas pdfCanvas = new PdfCanvas(page);
            pdfCanvas.ConcatMatrix(0, -1, 1, 0, startX, startY);

            // ---- Build Canvas ----
            Canvas canvas = new Canvas(pdfCanvas, new Rectangle(0, 0, pageHeight, pageWidth));

            // ---- Add Table ----
            table.SetFixedPosition(0, 0, 300);

            foreach (var row in table.GetChildren())
            {
                if (row is iText.Layout.Element.Cell cell)
                {
                    cell
                        .SetPadding(0)
                        .SetMargin(0)
                        .SetTextAlignment(TextAlignment.CENTER);
                }
                else if (row is iText.Layout.Element.IBlockElement block && block is iText.Layout.Element.Table innerTable)
                {
                    // Optional: recursively handle nested tables if you ever use them
                    foreach (var innerCell in innerTable.GetChildren().OfType<Cell>())
                    {
                        innerCell
                            .SetPadding(0)
                            .SetMargin(0)
                            .SetTextAlignment(TextAlignment.CENTER);
                    }
                }
            }

            canvas.Add(table);
            canvas.Close();

        }
    }
}