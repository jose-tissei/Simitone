#!/usr/bin/env node
// Checks Simitone HTML UI pages (Tools/HtmlUi/ui/*.html + sims-ui.css) against the Sims remaster UI redesign
// (docs/ui-redesign/UI-GUIDE.md and design-tokens.json). No dependencies.
//
//   node check-ui.mjs <page.html|dir> [--css sims-ui.css] [--tokens design-tokens.json] [--data sample.json] [--json]
//
// Exit code 1 when there is at least one ERROR. WARN and INFO never fail the run.
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const args = process.argv.slice(2);
const opt = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : null; };
const flag = (name) => args.includes(name);
const target = args.find((a, i) => !a.startsWith("--") && (i === 0 || !args[i - 1].startsWith("--")));
if (!target) { console.error("usage: node check-ui.mjs <page.html|dir> [--css file] [--tokens file] [--data file] [--json]"); process.exit(2); }

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, "..", "..", "..");
const tokensPath = opt("--tokens") ?? path.join(repo, "docs", "ui-redesign", "design-tokens.json");
const tokens = JSON.parse(fs.readFileSync(tokensPath, "utf8"));

// ---- token knowledge ----
const hexes = new Map(); // "#rrggbb" -> where it comes from
const addHex = (v, where) => { if (typeof v === "string" && /^#[0-9a-f]{6}$/i.test(v)) hexes.set(v.toUpperCase(), where); };
for (const [theme, groups] of Object.entries(tokens.themes)) for (const [g, o] of Object.entries(groups)) if (o && typeof o === "object") for (const [k, v] of Object.entries(o)) addHex(v, `${theme}.${g}.${k}`);
for (const [g, o] of Object.entries(tokens.shared)) if (o && typeof o === "object") for (const [k, v] of Object.entries(o)) { if (Array.isArray(v)) v.forEach((x, i) => addHex(x, `shared.${g}.${k}[${i}]`)); else addHex(v, `shared.${g}.${k}`); }
(tokens.shared.simIdentity || []).forEach((v, i) => addHex(v, `shared.simIdentity[${i}]`));
// constants the kit itself uses outside the token file (ink on danger/avatar, white rims)
const allowedLiteral = new Set(["#FFFFFF", "#FFF", "#3A0A06", "#04161B", "#000000"]);
const radii = new Set([...Object.values(tokens.radius), 0, 8, 12, 14]); // 8/12/14 appear on kit keys, stat boxes and rows
const kebab = (s) => s.replace(/[A-Z]/g, (c) => "-" + c.toLowerCase());
const special = { "accent.deep": "accent-deep", "text.primary": "text", "text.secondary": "text-2", "text.disabled": "text-off", "accent.accent": "accent", "accent.accent2": "accent-2", "focus.ring": "focus", "focus.gap": "focus-2" };
const knownVars = new Set(["scrim"]);
for (const [g, o] of Object.entries(tokens.themes.night)) if (o && typeof o === "object") for (const k of Object.keys(o)) knownVars.add(special[`${g}.${k}`] ?? kebab(k));
for (const k of Object.keys(tokens.shared.pill)) knownVars.add("pill-" + kebab(k));
for (const k of Object.keys(tokens.shared.iconButton)) knownVars.add("icon-btn-" + kebab(k));
for (const k of Object.keys(tokens.shared.pie)) knownVars.add("pie-" + kebab(k));
for (let i = 1; i <= 6; i++) knownVars.add("sim-" + i);
for (const k of Object.keys(tokens.spacing)) knownVars.add("sp-" + k);
for (const k of Object.keys(tokens.radius)) knownVars.add("r-" + kebab(k));
for (const s of ["good", "warn", "bad", "info"]) { knownVars.add(s + "-a16"); knownVars.add(s + "-a8"); }
const panelWidths = new Set([tokens.size.panel.S, tokens.size.panel.M, tokens.size.panel.L]);
const maxHeight = tokens.size.panel.maxHeight;
const minHit = tokens.size.minHit, smallHit = tokens.size.buttonSmall;
const minEssential = tokens.type.minEssentialSize;

// ---- findings ----
const findings = [];
const add = (level, rule, file, msg, where = "") => findings.push({ level, rule, file, msg, where });

// ---- css ----
function parseCss(text) {
  text = text.replace(/\/\*[\s\S]*?\*\//g, "");
  const rules = [];
  const re = /([^{}]+)\{([^{}]*)\}/g; let m;
  while ((m = re.exec(text))) {
    const decls = {};
    for (const d of m[2].split(";")) { const i = d.indexOf(":"); if (i > 0) decls[d.slice(0, i).trim().toLowerCase()] = d.slice(i + 1).trim(); }
    for (const sel of m[1].split(",")) rules.push({ selector: sel.trim(), decls });
  }
  return rules;
}

// ---- html ----
function parseHtml(html) {
  html = html.replace(/<!--[\s\S]*?-->/g, "");                // template markers and comments
  const root = { tag: "#root", attrs: {}, kids: [], text: "" };
  const stack = [root];
  const re = /<\/?([a-zA-Z][\w-]*)([^>]*?)(\/?)>|([^<]+)/g; let m;
  const voids = new Set(["img", "br", "hr", "input", "meta", "link"]);
  while ((m = re.exec(html))) {
    if (m[4] !== undefined) { stack[stack.length - 1].text += m[4]; continue; }
    const closing = m[0].startsWith("</");
    if (closing) { if (stack.length > 1) stack.pop(); continue; }
    const attrs = {}; const are = /([\w:-]+)\s*=\s*"([^"]*)"/g; let a;
    while ((a = are.exec(m[2]))) attrs[a[1]] = a[2];
    const el = { tag: m[1].toLowerCase(), attrs, kids: [], text: "", parent: stack[stack.length - 1] };
    el.parent.kids.push(el);
    if (!voids.has(el.tag) && !m[3]) stack.push(el);
  }
  return root;
}
const classesOf = (el) => (el.attrs.class || "").replace(/\{\{[^}]*\}\}/g, " ").split(/\s+/).filter(Boolean);
function* walk(el) { for (const k of el.kids) { yield k; yield* walk(k); } }
const textOf = (el) => (el.text + el.kids.map(textOf).join(" ")).replace(/\{\{[^}]*\}\}/g, "x").trim();

function matches(sel, el) {
  const last = sel.trim().split(/\s+/).pop();                   // descendant combinators: only the last compound is checked
  const m = last.match(/^([a-z][\w-]*)?((?:\.[\w-]+)*)((?::[\w-]+)*)$/i);
  if (!m || (m[3] && m[3] !== ":hover")) return false;
  if (m[3]) return false;                                       // :hover rules do not define the resting size
  if (m[1] && m[1] !== el.tag) return false;
  const need = m[2].split(".").filter(Boolean); const have = classesOf(el);
  return need.every((c) => have.includes(c)) && (need.length > 0 || !!m[1]);
}
function styleOf(el, rules) {
  const out = {};
  for (const r of rules) if (matches(r.selector, el)) Object.assign(out, r.decls);
  if (el.attrs.style) for (const d of el.attrs.style.split(";")) { const i = d.indexOf(":"); if (i > 0) out[d.slice(0, i).trim().toLowerCase()] = d.slice(i + 1).trim(); }
  return out;
}
const px = (v) => { const m = /^(-?[\d.]+)px$/.exec((v || "").trim()); return m ? parseFloat(m[1]) : null; };

function checkFile(file) {
  const html = fs.readFileSync(file, "utf8");
  const cssFile = opt("--css") ?? path.join(path.dirname(file), "sims-ui.css");
  const cssText = (fs.existsSync(cssFile) ? fs.readFileSync(cssFile, "utf8") : "") + "\n" + [...html.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/gi)].map((m) => m[1]).join("\n");
  const rules = parseCss(cssText);
  const rel = path.relative(process.cwd(), file);

  // R1/R14/R9: values in CSS and inline styles
  const allDecls = [...rules.map((r) => ({ where: r.selector, d: r.decls })), ...[...html.matchAll(/style="([^"]*)"/g)].map((m) => ({ where: "inline style", d: Object.fromEntries(m[1].split(";").filter((x) => x.includes(":")).map((x) => [x.split(":")[0].trim().toLowerCase(), x.slice(x.indexOf(":") + 1).trim()])) }))];
  const unsupported = { "display:grid": /grid/, "box-shadow": null, transition: null, animation: null, "backdrop-filter": null, "clip-path": null };
  const seen = new Set();
  for (const { where, d } of allDecls) for (const [prop, val] of Object.entries(d)) {
    for (const h of val.matchAll(/#([0-9a-f]{3}|[0-9a-f]{6})\b/gi)) {
      const hex = "#" + h[1].toUpperCase();
      const key = where + prop + hex; if (seen.has(key)) continue; seen.add(key);
      if (hexes.has(hex)) add("WARN", "token-colour", rel, `${hex} is token ${hexes.get(hex)}; use var(--...) so both themes work`, `${where} { ${prop} }`);
      else if (!allowedLiteral.has(hex)) add("ERROR", "unknown-colour", rel, `${hex} is not in design-tokens.json`, `${where} { ${prop} }`);
    }
    for (const v of val.replace(/\{\{[^}]*\}\}/g, "1").matchAll(/var\(\s*--([\w-]+)/g)) if (!knownVars.has(v[1])) add("ERROR", "unknown-token", rel, `var(--${v[1]}) is not a design token`, `${where} { ${prop} }`);
    if (/color-mix|grid|backdrop-filter|clip-path/.test(prop + " " + val) || prop === "box-shadow" || prop.startsWith("transition") || prop.startsWith("animation"))
      add("INFO", "engine-support", rel, `${prop}: ${val.slice(0, 40)} is not drawn by the in-game HTML engine (fine for web mock-ups)`, where);
    if (prop === "font-family" && !/Fredoka|Montserrat/i.test(val)) add("ERROR", "font", rel, `font-family ${val}: only Fredoka (display) and Montserrat (ui)`, where);
    if (prop === "border-radius") for (const r of val.split(/\s+/)) { const n = px(r); if (n !== null && !radii.has(n) && n < 100) add("WARN", "radius", rel, `radius ${n}px is not in the token scale (${[...radii].sort((a, b) => a - b).join(", ")})`, where); }
    if (prop === "font-size") {
      const n = px(val);
      const essentialOk = /t-over|t-cap|badge|\bkey\b|caption|overline/.test(where);
      if (n !== null && n < minEssential && !essentialOk) add("ERROR", "min-font", rel, `font-size ${n}px < ${minEssential}px (only captions, overlines, badges and key caps may be smaller)`, where);
    }
  }

  const tree = parseHtml(html);
  for (const el of walk(tree)) {
    const cls = classesOf(el);
    // R3 hit targets
    if (el.attrs["data-action"] !== undefined) {
      const st = styleOf(el, rules);
      const h = px(st["min-height"]) ?? px(st["height"]); const w = px(st["min-width"]) ?? px(st["width"]);
      const label = `${el.tag}.${cls.join(".")} data-action="${el.attrs["data-action"]}"`;
      if (h === null && w === null) add("WARN", "hit-target", rel, "no fixed size found; make sure it is at least 48x48", label);
      else {
        const small = [h, w].filter((v) => v !== null).some((v) => v < smallHit);
        const mid = [h, w].filter((v) => v !== null).some((v) => v < minHit);
        if (small) add("ERROR", "hit-target", rel, `hit target ${w ?? "?"}x${h ?? "?"} is below ${smallHit}px`, label);
        else if (mid) add("WARN", "hit-target", rel, `hit target ${w ?? "?"}x${h ?? "?"}: ${smallHit}px is only for small buttons; the rule is ${minHit}px`, label);
      }
      // R8 icon-only controls need a name
      const onlyIcon = el.kids.length > 0 && el.kids.every((k) => classesOf(k).includes("ic")) && textOf(el) === "";
      if (onlyIcon && !el.attrs["aria-label"] && !el.attrs.title) add("ERROR", "icon-label", rel, "icon-only control without aria-label (tooltips and screen text need a name)", label);
    }
    // R7 status chips must not rely on colour alone
    if (cls.includes("chip") && cls.some((c) => ["good", "warn", "bad", "info"].includes(c))) {
      const hasIcon = [...walk(el)].some((k) => classesOf(k).includes("ic"));
      if (!hasIcon && textOf(el) === "") add("ERROR", "colour-only", rel, "status chip without icon or label", `chip.${cls.join(".")}`);
      else if (!hasIcon) add("WARN", "colour-only", rel, "status chip has a label but no icon (guide: colour + icon + label)", `chip.${cls.join(".")}: "${textOf(el).slice(0, 30)}"`);
    }
    // R6 dialogs
    if (cls.includes("dlg")) {
      const st = styleOf(el, rules); const w = px(st.width), h = px(st.height);
      if (w !== null && !panelWidths.has(w)) add("ERROR", "dialog-size", rel, `dialog width ${w}px: use S ${tokens.size.panel.S}, M ${tokens.size.panel.M} or L ${tokens.size.panel.L}`, "dlg");
      if (h !== null && h > maxHeight) add("ERROR", "dialog-size", rel, `dialog height ${h}px > ${maxHeight}px`, "dlg");
      if (!/esc/i.test(textOf(el))) add("WARN", "esc-hint", rel, "modal without an Esc hint (Esc must always close the top modal)", "dlg");
      if (![...walk(el)].some((k) => k.attrs["data-action"] === "close")) add("ERROR", "close-action", rel, "modal without a data-action=\"close\" control", "dlg");
    }
  }
  // R11 one primary action
  const primaries = [...walk(tree)].filter((e) => { const c = classesOf(e); return c.includes("btn") && c.includes("primary"); }).length;
  if (primaries > 1) add("ERROR", "one-primary", rel, `${primaries} primary buttons: one primary action per surface`, "btn.primary");

  // R15 template paths exist in sample data
  const dataFile = opt("--data") ?? file.replace(/\.html$/, ".sample.json");
  if (fs.existsSync(dataFile)) {
    const data = JSON.parse(fs.readFileSync(dataFile, "utf8"));
    const has = (obj, p) => p.split(".").every((k) => { if (obj && typeof obj === "object" && k in obj) { obj = obj[k]; return true; } return false; });
    const walkScopes = (ptr, text) => text; // scopes are not tracked: a path counts as known when it exists anywhere in the sample
    const flat = new Set(); const collect = (o) => { if (Array.isArray(o)) o.forEach(collect); else if (o && typeof o === "object") for (const [k, v] of Object.entries(o)) { flat.add(k); collect(v); } };
    collect(data);
    for (const m of html.matchAll(/\{\{\s*([\w.#]+)\s*\}\}|<!--#(?:if|each)\s+([\w.]+)/g)) {
      const p = m[1] ?? m[2]; if (p === "." || p === "#") continue;
      if (!flat.has(p.split(".").pop())) add("WARN", "template-field", rel, `{{${p}}} is not present in ${path.basename(dataFile)}`, "");
    }
  }
}

const files = fs.statSync(target).isDirectory() ? fs.readdirSync(target).filter((f) => f.endsWith(".html")).map((f) => path.join(target, f)) : [target];
for (const f of files) checkFile(path.resolve(f));

const order = { ERROR: 0, WARN: 1, INFO: 2 };
findings.sort((a, b) => order[a.level] - order[b.level] || a.rule.localeCompare(b.rule));
if (flag("--json")) console.log(JSON.stringify(findings, null, 2));
else {
  for (const f of findings) console.log(`${f.level.padEnd(5)} ${f.rule.padEnd(14)} ${f.msg}${f.where ? "  [" + f.where + "]" : ""}`);
  const n = (l) => findings.filter((f) => f.level === l).length;
  console.log(`\n${files.length} page(s): ${n("ERROR")} error(s), ${n("WARN")} warning(s), ${n("INFO")} note(s)`);
}
process.exit(findings.some((f) => f.level === "ERROR") ? 1 : 0);
