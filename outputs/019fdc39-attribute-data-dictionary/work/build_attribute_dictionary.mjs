import fs from "node:fs/promises";
import path from "node:path";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const workDir = path.dirname(new URL(import.meta.url).pathname);
const outputDir = path.resolve(workDir, "..");
const metadataPath = path.join(workDir, "metadata.json");
const outputPath = path.join(outputDir, "ColdChainX_Attribute_Data_Dictionary.xlsx");

const metadata = JSON.parse(await fs.readFile(metadataPath, "utf8"));
const tableCount = metadata.Tables.length;
const attributeCount = metadata.Tables.reduce((sum, table) => sum + table.Columns.length, 0);
const fkColumnCount = metadata.Tables.reduce(
  (sum, table) => sum + table.Columns.filter((column) => column.ForeignKeys.length > 0).length,
  0,
);

const workbook = Workbook.create();
const sheet = workbook.worksheets.add("Attribute Dictionary");
sheet.showGridLines = false;

const titleRow = 1;
const headerRow = 3;
const firstDataRow = 4;
const headers = ["No", "Table Name", "Attribute Name", "Data Type", "Not Null", "PK/FK"];

const rows = [
  ["Attribute Data Dictionary", "", "", "", "", ""],
  ["", "", "", "", "", ""],
  headers,
];

const tableRanges = [];
let currentRow = firstDataRow;
for (const table of metadata.Tables) {
  const startRow = currentRow;
  table.Columns.forEach((column, index) => {
    const isForeignKey = column.ForeignKeys.length > 0;
    const keyType = column.IsPrimaryKey && isForeignKey
      ? "PK/FK"
      : column.IsPrimaryKey
        ? "PK"
        : isForeignKey
          ? "FK"
          : "";

    rows.push([
      index === 0 ? table.No : "",
      index === 0 ? table.TableName : "",
      column.AttributeName,
      column.DataType,
      column.NotNull ? "Yes" : "No",
      keyType,
    ]);
    currentRow += 1;
  });
  tableRanges.push({ startRow, endRow: currentRow - 1, no: table.No });
}

const lastRow = rows.length;
sheet.getRange(`A1:F${lastRow}`).values = rows;

for (const tableRange of tableRanges) {
  if (tableRange.endRow > tableRange.startRow) {
    sheet.mergeCells(`A${tableRange.startRow}:A${tableRange.endRow}`);
    sheet.mergeCells(`B${tableRange.startRow}:B${tableRange.endRow}`);
  }
}

sheet.getRange(`A${titleRow}:F${titleRow}`).format = {
  fill: "#FFFFFF",
  font: { bold: true, italic: true, underline: true, color: "#000000", size: 18 },
  horizontalAlignment: "left",
  verticalAlignment: "center",
};
sheet.getRange(`A${titleRow}:F${titleRow}`).format.rowHeight = 32;
sheet.getRange("A2:F2").format.rowHeight = 12;

const headerRange = sheet.getRange(`A${headerRow}:F${headerRow}`);
headerRange.format = {
  fill: "#FCE4D6",
  font: { bold: true, color: "#000000", size: 11 },
  horizontalAlignment: "left",
  verticalAlignment: "center",
  wrapText: true,
};
headerRange.format.borders = { preset: "all", style: "medium", color: "#000000" };
headerRange.format.rowHeight = 48;

const bodyRange = sheet.getRange(`A${firstDataRow}:F${lastRow}`);
bodyRange.format = {
  fill: "#FFFFFF",
  font: { color: "#000000", size: 10 },
  verticalAlignment: "center",
  wrapText: true,
};
bodyRange.format.borders = { preset: "all", style: "thin", color: "#000000" };
bodyRange.format.rowHeight = 20;
sheet.getRange(`A${headerRow}:F${lastRow}`).format.borders = {
  top: { style: "medium", color: "#000000" },
  bottom: { style: "medium", color: "#000000" },
  left: { style: "medium", color: "#000000" },
  right: { style: "medium", color: "#000000" },
  insideHorizontal: { style: "thin", color: "#000000" },
  insideVertical: { style: "thin", color: "#000000" },
};

sheet.getRange(`A${firstDataRow}:A${lastRow}`).format = {
  horizontalAlignment: "center",
  verticalAlignment: "top",
  font: { color: "#000000", size: 10 },
};
sheet.getRange(`B${firstDataRow}:B${lastRow}`).format = {
  horizontalAlignment: "left",
  verticalAlignment: "top",
  font: { color: "#000000", size: 10 },
  wrapText: true,
};
sheet.getRange(`C${firstDataRow}:D${lastRow}`).format.horizontalAlignment = "left";
sheet.getRange(`E${firstDataRow}:E${lastRow}`).format.horizontalAlignment = "left";
sheet.getRange(`F${firstDataRow}:F${lastRow}`).format = {
  horizontalAlignment: "center",
  verticalAlignment: "center",
  font: { color: "#000000", size: 10 },
  wrapText: true,
};

sheet.getRange(`A${firstDataRow}:A${lastRow}`).format.columnWidth = 8;
sheet.getRange(`B${firstDataRow}:B${lastRow}`).format.columnWidth = 21;
sheet.getRange(`C${firstDataRow}:C${lastRow}`).format.columnWidth = 34;
sheet.getRange(`D${firstDataRow}:D${lastRow}`).format.columnWidth = 29;
sheet.getRange(`E${firstDataRow}:E${lastRow}`).format.columnWidth = 10;
sheet.getRange(`F${firstDataRow}:F${lastRow}`).format.columnWidth = 8;

const topCheck = await workbook.inspect({
  kind: "table",
  range: `Attribute Dictionary!A1:F18`,
  include: "values,formulas",
  tableMaxRows: 18,
  tableMaxCols: 6,
  maxChars: 7000,
});
console.log(topCheck.ndjson);

const bottomCheck = await workbook.inspect({
  kind: "table",
  range: `Attribute Dictionary!A${Math.max(firstDataRow, lastRow - 12)}:F${lastRow}`,
  include: "values,formulas",
  tableMaxRows: 15,
  tableMaxCols: 6,
  maxChars: 7000,
});
console.log(bottomCheck.ndjson);

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
  options: { useRegex: true, maxResults: 100 },
  summary: "final formula error scan",
});
console.log(errors.ndjson);

const previewRanges = [
  { name: "preview-top.png", range: `A1:F42` },
  {
    name: "preview-middle.png",
    range: `A${Math.max(firstDataRow, Math.floor(lastRow / 2) - 18)}:F${Math.min(lastRow, Math.floor(lastRow / 2) + 18)}`,
  },
  { name: "preview-bottom.png", range: `A${Math.max(firstDataRow, lastRow - 37)}:F${lastRow}` },
];

await fs.mkdir(outputDir, { recursive: true });
for (const previewSpec of previewRanges) {
  const preview = await workbook.render({
    sheetName: "Attribute Dictionary",
    range: previewSpec.range,
    scale: 1.5,
    format: "png",
  });
  await fs.writeFile(
    path.join(outputDir, previewSpec.name),
    new Uint8Array(await preview.arrayBuffer()),
  );
}

const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);

console.log(JSON.stringify({
  outputPath,
  tableCount,
  attributeCount,
  fkColumnCount,
  lastRow,
}));
