// Test sonuç dosyalarını tek biçimde özetler (test.ps1 ve CI için):
//   node ozet.mjs <ctrf|vitest|eslint|playwright> <dosya>
// Çıktı (JSON): { passed, failed, failures: [{ name, reason, screenshot }], warnings: [metin] }
// PowerShell 5.1'in ConvertFrom-Json'u büyük dosyalarda zorlandığı için ayrıştırma burada yapılır.
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";

const [kind, file] = process.argv.slice(2);
const plain = (text = "") => text.replace(/\x1b\[[0-9;]*m/g, "");

/** Hata metninin kısa nedeni: ilk anlamlı satır (otomatik kontrollerde ilk sorun). */
function reason(text) {
  const lines = plain(text)
    .split("\n")
    .map((l) => l.trim())
    .filter(Boolean);
  const issue = lines.find((l) => l.startsWith("- ["));
  // expect hatalarında beklenen/gelen değer de yazılır.
  const values = lines.filter((l) => /^(Expected|Received|Locator):/.test(l)).slice(0, 3);
  const first = issue ?? [lines[0] ?? "", ...values].join(" · ");
  return first.length > 300 ? `${first.slice(0, 297)}…` : first;
}

function ctrf(json) {
  const tests = json.results?.tests ?? [];
  const failed = tests.filter((t) => t.status === "failed");
  return {
    passed: tests.filter((t) => t.status === "passed").length,
    failed: failed.length,
    failures: failed.map((t) => ({ name: t.name, reason: reason(t.message ?? t.trace ?? ""), screenshot: null })),
  };
}

function vitest(json) {
  const failures = [];
  for (const f of json.testResults ?? [])
    for (const a of f.assertionResults ?? [])
      if (a.status === "failed")
        failures.push({ name: `${path.basename(f.name)} › ${a.fullName}`, reason: reason(a.failureMessages?.join("\n")), screenshot: null });
  return { passed: json.numPassedTests ?? 0, failed: json.numFailedTests ?? failures.length, failures };
}

function eslint(json) {
  const failures = [];
  for (const f of json)
    for (const m of f.messages ?? [])
      if (m.severity === 2)
        failures.push({ name: `${path.relative(process.cwd(), f.filePath)}:${m.line}`, reason: `${m.message} (${m.ruleId})`, screenshot: null });
  return { passed: json.filter((f) => f.errorCount === 0).length, failed: failures.length, failures };
}

function playwright(json) {
  let passed = 0;
  const failures = [];
  const warnings = new Set();
  const walk = (suite, titles) => {
    for (const child of suite.suites ?? []) walk(child, [...titles, child.title]);
    for (const spec of suite.specs ?? [])
      for (const t of spec.tests ?? []) {
        if (t.status === "expected" || t.status === "flaky") passed += 1;
        for (const a of t.annotations ?? []) if (a.type === "uyarı") warnings.add(`[${t.projectName}] ${a.description}`);
        if (t.status !== "unexpected") continue;
        const last = t.results.at(-1) ?? {};
        const shot = (last.attachments ?? []).find((a) => a.name === "screenshot" && a.path);
        failures.push({
          name: `${[...titles, spec.title].filter(Boolean).join(" › ")} [${t.projectName}]`,
          reason: reason((last.errors ?? []).map((e) => e.message).join("\n") || last.error?.message),
          screenshot: shot ? path.relative(path.resolve(".."), shot.path) : null,
        });
      }
  };
  for (const s of json.suites ?? []) walk(s, [s.title]);
  return { passed, failed: failures.length, failures, warnings: [...warnings] };
}

if (!file || !existsSync(file)) {
  console.log(JSON.stringify({ passed: 0, failed: 1, failures: [{ name: kind, reason: `Sonuç dosyası yok: ${file}`, screenshot: null }] }));
} else {
  const json = JSON.parse(readFileSync(file, "utf8"));
  const parse = { ctrf, vitest, eslint, playwright }[kind];
  if (!parse) throw new Error(`Bilinmeyen tür: ${kind}`);
  console.log(JSON.stringify(parse(json)));
}
