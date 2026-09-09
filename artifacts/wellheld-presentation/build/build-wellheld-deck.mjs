import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { Presentation, PresentationFile } from "@oai/artifact-tool";

const workspaceDir = "C:\\Users\\Johan\\source\\repos\\Virtual Company";
const SKILL_DIR = "C:\\Users\\Johan\\.codex\\plugins\\cache\\openai-primary-runtime\\presentations\\26.904.11930\\skills\\presentations";
const TMP_DIR = path.join(workspaceDir, "artifacts", "wellheld-presentation", "build", "output");
const FINAL_PPTX = path.join(workspaceDir, "artifacts", "wellheld-presentation", "output", "wellheld-overview.pptx");
const RUNTIME_PYTHON = "C:\\Users\\Johan\\.cache\\codex-runtimes\\codex-primary-runtime\\dependencies\\python\\python.exe";
const fontFamily = "Segoe UI";

const colors = {
  navy: "#17233B",
  blue: "#356AE6",
  paper: "#F6F3ED",
  white: "#FCFCFB",
  moss: "#4F7865",
  amber: "#C98528",
  ink: "#4C5567",
  paleBlue: "#E9EFFD",
  paleAmber: "#F4E8D4",
};

await fs.mkdir(TMP_DIR, { recursive: true });
await fs.mkdir(path.dirname(FINAL_PPTX), { recursive: true });

function addText(slide, text, position, style = {}) {
  const box = slide.shapes.add({
    geometry: "textbox",
    position,
    fill: "none",
    line: { fill: "none", width: 0 },
  });
  box.text = text;
  box.text.style = {
    typeface: fontFamily,
    fontSize: style.fontSize ?? 20,
    bold: style.bold ?? false,
    color: style.color ?? colors.navy,
    autoFit: "none",
    verticalAlignment: style.verticalAlignment ?? "top",
    alignment: style.alignment ?? "left",
  };
  return box;
}

function addRect(slide, position, fill, radius = 0) {
  return slide.shapes.add({
    geometry: radius > 0 ? "roundRect" : "rect",
    position,
    fill,
    line: { fill: "none", width: 0 },
    ...(radius > 0 ? { borderRadius: radius } : {}),
  });
}

async function addPng(slide, filename, position, alt, fit = "cover") {
  const blob = new Uint8Array(await fs.readFile(filename));
  return slide.images.add({ blob, contentType: "image/png", alt, fit, position });
}

function addRule(slide, left, top, width, color = colors.blue) {
  addRect(slide, { left, top, width, height: 5 }, color);
}

function addNotes(slide, text) {
  slide.speakerNotes.textFrame.setText(text);
}

const presentation = Presentation.create({ slideSize: { width: 1280, height: 720 } });

// Slide 1: concise cover and definition.
{
  const slide = presentation.slides.add();
  slide.background.fill = colors.paper;
  await addPng(
    slide,
    path.join(workspaceDir, "artifacts", "wellheld-presentation", "assets", "wellheld-cover.png"),
    { left: 610, top: 0, width: 670, height: 720 },
    "Business documents and operational signals settling into an open, controlled structure"
  );
  addRect(slide, { left: 0, top: 0, width: 655, height: 720 }, colors.paper);
  addRule(slide, 72, 86, 84);
  addText(slide, "Wellheld", { left: 72, top: 128, width: 500, height: 92 }, { fontSize: 68, bold: true });
  addText(slide, "Your company, well held", { left: 72, top: 241, width: 490, height: 86 }, { fontSize: 31, bold: true, color: colors.blue });
  addText(
    slide,
    "A governed AI team for finance, sales, marketing and support, working under your direction.",
    { left: 72, top: 358, width: 470, height: 118 },
    { fontSize: 22, color: colors.ink }
  );
  addText(slide, "CALM OPERATIONAL INTELLIGENCE", { left: 72, top: 604, width: 430, height: 30 }, { fontSize: 13, bold: true, color: colors.navy });
  addText(slide, "01", { left: 540, top: 644, width: 42, height: 24 }, { fontSize: 12, bold: true, color: colors.ink, alignment: "right" });
  addNotes(slide, "Product description and visual direction are based on the Wellheld brand brief supplied for this presentation. Illustration generated specifically for this deck.");
}

// Slide 2: product capabilities and business value.
{
  const slide = presentation.slides.add();
  slide.background.fill = colors.white;
  addText(slide, "One operating team, clear human control", { left: 72, top: 54, width: 1130, height: 62 }, { fontSize: 38, bold: true });
  addRule(slide, 72, 129, 68, colors.amber);

  await addPng(
    slide,
    path.join(workspaceDir, "artifacts", "wellheld-presentation", "assets", "wellheld-value.png"),
    { left: 606, top: 150, width: 620, height: 490 },
    "A company owner overseeing four coordinated operating work streams and a visible human approval point"
  );

  addText(slide, "What you can do", { left: 72, top: 170, width: 430, height: 38 }, { fontSize: 21, bold: true, color: colors.blue });
  addText(slide, "Run finance, sales, marketing and support from one shared operating view.", { left: 72, top: 218, width: 450, height: 70 }, { fontSize: 19, color: colors.ink });
  addText(slide, "Review source evidence and approve consequential actions before they leave the company.", { left: 72, top: 300, width: 450, height: 82 }, { fontSize: 19, color: colors.ink });
  addText(slide, "Let specialist agents prepare work and coordinate handoffs while you retain the right to pause or take control.", { left: 72, top: 396, width: 455, height: 92 }, { fontSize: 19, color: colors.ink });

  addRect(slide, { left: 72, top: 523, width: 454, height: 104 }, colors.paleAmber, 12);
  addText(slide, "Business value", { left: 94, top: 541, width: 180, height: 30 }, { fontSize: 16, bold: true, color: colors.amber });
  addText(slide, "Fewer missed priorities. Faster evidence-backed decisions. More operating capacity with visible limits.", { left: 94, top: 574, width: 400, height: 45 }, { fontSize: 17, bold: true, color: colors.navy });
  addText(slide, "02", { left: 1166, top: 662, width: 42, height: 22 }, { fontSize: 12, bold: true, color: colors.ink, alignment: "right" });
  addNotes(slide, "The benefit statements describe the intended Wellheld product experience and contain no external quantitative claims. Illustration generated specifically for this deck.");
}

// Slide 3: onboarding path.
{
  const slide = presentation.slides.add();
  slide.background.fill = colors.paper;
  addText(slide, "Starting with Wellheld", { left: 72, top: 54, width: 580, height: 62 }, { fontSize: 40, bold: true });
  addRule(slide, 72, 129, 68, colors.blue);

  await addPng(
    slide,
    path.join(workspaceDir, "artifacts", "wellheld-presentation", "assets", "wellheld-start.png"),
    { left: 602, top: 142, width: 626, height: 496 },
    "Company information entering an open operating frame and specialist roles organizing the work"
  );

  const steps = [
    ["01", "Company workspace", "Add the company details, goals, working policies and the information Wellheld may use."],
    ["02", "AI team and limits", "Choose the specialist roles you need, then define approval points and operating boundaries."],
    ["03", "First priorities", "Connect the relevant sources and review the first recommended work before expanding further."],
  ];

  let top = 171;
  for (const [number, heading, body] of steps) {
    addText(slide, number, { left: 72, top, width: 55, height: 32 }, { fontSize: 18, bold: true, color: colors.amber });
    addText(slide, heading, { left: 139, top: top - 2, width: 385, height: 33 }, { fontSize: 21, bold: true, color: colors.navy });
    addText(slide, body, { left: 139, top: top + 38, width: 405, height: 67 }, { fontSize: 17, color: colors.ink });
    top += 137;
  }

  addRect(slide, { left: 72, top: 590, width: 474, height: 53 }, colors.paleBlue, 10);
  addText(slide, "Most teams begin with one area and expand after the first operating rhythm is established.", { left: 92, top: 604, width: 434, height: 34 }, { fontSize: 15, bold: true, color: colors.blue });
  addText(slide, "03", { left: 1166, top: 662, width: 42, height: 22 }, { fontSize: 12, bold: true, color: colors.ink, alignment: "right" });
  addNotes(slide, "Suggested onboarding sequence based on the Wellheld product concept supplied for this presentation. Illustration generated specifically for this deck.");
}

// Private previews for visual review.
for (let index = 0; index < presentation.slides.items.length; index += 1) {
  const slide = presentation.slides.items[index];
  const preview = await presentation.export({ slide, format: "png", scale: 1 });
  await fs.writeFile(path.join(TMP_DIR, `slide-${index + 1}.png`), new Uint8Array(await preview.arrayBuffer()));
  const layout = await slide.export({ format: "layout" });
  await fs.writeFile(path.join(TMP_DIR, `slide-${index + 1}.layout.json`), await layout.text());
}

const { finalizePresentation } = await import(pathToFileURL(
  path.join(SKILL_DIR, "container_tools", "artifact_tool_utils.mjs")
).href);

const stagingDir = path.join(workspaceDir, ".codex-finalizer", "wellheld-presentation");
await fs.mkdir(stagingDir, { recursive: true });
const candidatePath = path.join(stagingDir, "candidate.pptx");
await (await PresentationFile.exportPptx(presentation)).save(candidatePath);

await finalizePresentation({
  explicitTotalSlideCount: 3,
  requiredNativeTableOwnerSlides: [],
  requiredNativeChartOwnerSlides: [],
  workspaceDir,
  candidatePath,
  finalPath: FINAL_PPTX,
  pythonExecutable: RUNTIME_PYTHON,
  integrityValidatorPath: path.join(SKILL_DIR, "container_tools", "inspect_presentation_package_integrity.py"),
  layoutValidatorPath: path.join(SKILL_DIR, "container_tools", "inspect_presentation_layout_geometry.py"),
  layoutArgs: ["--expected-slide-size-emu", "12192000,6858000", "--validate-bullet-geometry", "--validate-heading-fit"],
  fontPolicy: { basis: "design", families: [fontFamily] },
  verifyArtifactToolImport: true,
  receiptPath: path.join(stagingDir, "wellheld-overview.pptx.validation.json"),
});

console.log(FINAL_PPTX);


