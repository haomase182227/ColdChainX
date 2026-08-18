import fs from "node:fs/promises";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

process.on("uncaughtException", (error) => {
  console.error(`UNCAUGHT_EXCEPTION=${error?.message ?? String(error)}`);
  process.exit(1);
});
process.on("unhandledRejection", (error) => {
  console.error(`UNHANDLED_REJECTION=${error?.message ?? String(error)}`);
  process.exit(1);
});

const projectRoot = "/Users/macbuituananh/ColdChainX";
const sourcePath = `${projectRoot}/docs/5.1_Business_Rules.md`;
const outputDir = `${projectRoot}/outputs/coldchainx-business-rules-20260806`;
const outputPath = `${outputDir}/ColdChainX_Business_Rules.xlsx`;

const source = await fs.readFile(sourcePath, "utf8");
const rules = source
  .split(/\r?\n/)
  .map((line) => line.match(/^\|\s*(BR-\d+)\s*\|\s*(.*?)\s*\|$/))
  .filter(Boolean)
  .map((match) => [match[1], match[2]]);

if (rules.length !== 139) {
  throw new Error(`Expected 139 rules, found ${rules.length}`);
}

for (let index = 0; index < rules.length; index += 1) {
  const expectedId = `BR-${String(index + 1).padStart(2, "0")}`;
  if (rules[index][0] !== expectedId) {
    throw new Error(`Expected ${expectedId}, found ${rules[index][0]}`);
  }
}

const workbook = Workbook.create();
const sheet = workbook.worksheets.add("Business Rules");
sheet.showGridLines = false;

sheet.mergeCells("A1:B1");
sheet.getRange("A1").values = [["5. Requirement Appendix"]];
sheet.mergeCells("A2:B2");
sheet.getRange("A2").values = [["5.1 Business Rules"]];

sheet.getRange("A4:B4").values = [["ID", "Rule Definition"]];
sheet.getRange(`A5:B${rules.length + 4}`).values = rules;

const table = sheet.tables.add(`A4:B${rules.length + 4}`, true, "BusinessRulesTable");
table.style = "TableStyleLight1";
table.showHeaders = true;
table.showFilterButton = true;

sheet.getRange("A1:B1").format = {
  fill: "#FFFFFF",
  font: { name: "Aptos Display", size: 18, bold: true, color: "#111827" },
  horizontalAlignment: "left",
  verticalAlignment: "center",
};
sheet.getRange("A1:B1").format.rowHeight = 30;

sheet.getRange("A2:B2").format = {
  fill: "#FFFFFF",
  font: { name: "Aptos Display", size: 14, bold: true, color: "#1F4E78" },
  horizontalAlignment: "left",
  verticalAlignment: "center",
};
sheet.getRange("A2:B2").format.rowHeight = 25;
sheet.getRange("A3:B3").format.rowHeight = 8;

sheet.getRange("A4:B4").format = {
  fill: "#FCE4D6",
  font: { name: "Aptos", size: 11, bold: true, color: "#172033" },
  horizontalAlignment: "center",
  verticalAlignment: "center",
  wrapText: true,
  borders: { preset: "all", style: "medium", color: "#7F8C8D" },
};
sheet.getRange("A4:B4").format.rowHeight = 30;

const dataRange = sheet.getRange(`A5:B${rules.length + 4}`);
dataRange.format = {
  fill: "#FFFFFF",
  font: { name: "Aptos", size: 11, color: "#111827" },
  verticalAlignment: "top",
  wrapText: true,
  borders: { preset: "all", style: "thin", color: "#B7B7B7" },
};

sheet.getRange(`A5:A${rules.length + 4}`).format = {
  font: { name: "Aptos", size: 11, bold: true, color: "#1F2937" },
  horizontalAlignment: "center",
  verticalAlignment: "top",
  wrapText: false,
  numberFormat: "@",
};
sheet.getRange(`B5:B${rules.length + 4}`).format = {
  font: { name: "Aptos", size: 11, color: "#111827" },
  horizontalAlignment: "left",
  verticalAlignment: "top",
  wrapText: true,
};

for (let index = 0; index < rules.length; index += 1) {
  const rowNumber = index + 5;
  const definitionLength = rules[index][1].length;
  const rowHeight = definitionLength <= 120 ? 29 : definitionLength <= 220 ? 43 : 57;
  sheet.getRange(`A${rowNumber}:B${rowNumber}`).format.rowHeight = rowHeight;
  if (index % 2 === 1) {
    sheet.getRange(`A${rowNumber}:B${rowNumber}`).format.fill = "#FFF9F5";
  }
}

sheet.getRange(`A4:B${rules.length + 4}`).format.borders = {
  insideHorizontal: { style: "thin", color: "#B7B7B7" },
  insideVertical: { style: "thin", color: "#9CA3AF" },
  top: { style: "medium", color: "#6B7280" },
  bottom: { style: "medium", color: "#6B7280" },
  left: { style: "medium", color: "#6B7280" },
  right: { style: "medium", color: "#6B7280" },
};

sheet.getRange(`A1:A${rules.length + 4}`).format.columnWidth = 14;
sheet.getRange(`B1:B${rules.length + 4}`).format.columnWidth = 105;
sheet.freezePanes.freezeRows(4);
sheet.freezePanes.freezeColumns(1);

await fs.mkdir(outputDir, { recursive: true });

const inspection = await workbook.inspect({
  kind: "table",
  range: "Business Rules!A1:B12",
  include: "values,formulas",
  tableMaxRows: 12,
  tableMaxCols: 2,
  maxChars: 5000,
});
console.log("INSPECTION");
console.log(inspection.ndjson);

const formulaErrors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
  options: { useRegex: true, maxResults: 50 },
  summary: "final formula error scan",
});
console.log("FORMULA_ERRORS");
console.log(formulaErrors.ndjson);

const topPreview = await workbook.render({
  sheetName: "Business Rules",
  range: "A1:B18",
  scale: 1.4,
  format: "png",
});
await fs.writeFile(`${outputDir}/preview-top.png`, new Uint8Array(await topPreview.arrayBuffer()));

const bottomPreview = await workbook.render({
  sheetName: "Business Rules",
  range: "A132:B143",
  scale: 1.4,
  format: "png",
});
await fs.writeFile(`${outputDir}/preview-bottom.png`, new Uint8Array(await bottomPreview.arrayBuffer()));

const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);

console.log(`EXPORTED=${outputPath}`);
console.log(`RULES=${rules.length}`);
