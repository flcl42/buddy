import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

const scriptsDirectory = path.dirname(fileURLToPath(import.meta.url));
const workspace = path.resolve(scriptsDirectory, "..");
const siteDirectory = path.join(workspace, "site");
const appSource = fs.readFileSync(path.join(siteDirectory, "app.js"), "utf8");

class FakeClassList {
  constructor(classes = []) {
    this.classes = new Set(classes);
  }

  contains(name) {
    return this.classes.has(name);
  }

  toggle(name, force) {
    const enabled = force === undefined ? !this.classes.has(name) : Boolean(force);
    if (enabled) {
      this.classes.add(name);
    } else {
      this.classes.delete(name);
    }
    return enabled;
  }
}

class FakeElement {
  constructor({ classes = [], dataset = {} } = {}) {
    this.classList = new FakeClassList(classes);
    this.dataset = dataset;
    this.hidden = false;
    this.textContent = "";
    this.attributes = new Map();
    this.listeners = new Map();
  }

  addEventListener(type, handler) {
    const handlers = this.listeners.get(type) || [];
    handlers.push(handler);
    this.listeners.set(type, handlers);
  }

  dispatch(type, event = {}) {
    for (const handler of this.listeners.get(type) || []) {
      handler(event);
    }
  }

  setAttribute(name, value) {
    this.attributes.set(name, String(value));
  }

  removeAttribute(name) {
    this.attributes.delete(name);
  }
}

function createCarousel() {
  const slides = Array.from({ length: 4 }, (_, index) =>
    new FakeElement({ classes: index === 0 ? ["is-active"] : [] }));
  slides.slice(1).forEach((slide) => { slide.hidden = true; });
  const dots = Array.from({ length: 4 }, (_, index) =>
    new FakeElement({ classes: index === 0 ? ["is-active"] : [], dataset: { carouselIndex: String(index) } }));
  dots[0].setAttribute("aria-current", "true");
  const previous = new FakeElement();
  const next = new FakeElement();
  const current = new FakeElement();
  current.textContent = "1";
  const carousel = new FakeElement();
  carousel.querySelectorAll = (selector) => selector === "[data-carousel-slide]" ? slides : dots;
  carousel.querySelector = (selector) => ({
    "[data-carousel-prev]": previous,
    "[data-carousel-next]": next,
    "[data-carousel-current]": current
  })[selector] || null;

  return { carousel, slides, dots, previous, next, current };
}

function runApp({ locale = "en", languages = ["en-US"], savedLocale = "", search = "", withCarousel = false } = {}) {
  const storage = new Map(savedLocale ? [["buddy-language", savedLocale]] : []);
  const redirects = [];
  const carouselState = withCarousel ? createCarousel() : null;
  const document = {
    body: { dataset: { locale, root: locale === "en" ? "." : ".." } },
    querySelector: () => null,
    querySelectorAll: (selector) => selector === "[data-carousel]" && carouselState ? [carouselState.carousel] : []
  };
  const window = {
    BUDDY_REPOSITORY: "",
    location: {
      hostname: "localhost",
      pathname: locale === "en" ? "/" : `/${locale}/`,
      search,
      assign: (url) => redirects.push({ kind: "assign", url }),
      replace: (url) => redirects.push({ kind: "replace", url })
    }
  };
  const context = vm.createContext({
    document,
    window,
    navigator: { languages, language: languages[0] || "" },
    localStorage: {
      getItem: (key) => storage.get(key) || null,
      setItem: (key, value) => storage.set(key, String(value))
    },
    URLSearchParams,
    Intl,
    fetch: () => Promise.reject(new Error("Unexpected fetch"))
  });

  vm.runInContext(appSource, context, { filename: "site/app.js" });
  return { storage, redirects, carouselState };
}

function pngDimensions(filePath) {
  const header = fs.readFileSync(filePath).subarray(0, 24);
  assert.equal(header.toString("ascii", 1, 4), "PNG", `${filePath} is not a PNG`);
  return { width: header.readUInt32BE(16), height: header.readUInt32BE(20) };
}

const pages = ["index.html", "es/index.html", "de/index.html", "be/index.html"];
const releaseAssets = [
  "Buddy-Setup.exe",
  "Buddy.exe",
  "Buddy-macOS-arm64-beta.zip",
  "Buddy-macOS-x64-beta.zip",
  "Buddy-Linux-x64-preview.deb",
  "Buddy-Linux-x64-preview.tar.gz"
];
const recordingCopy = {
  "index.html": /Pause-cut playback, seeking, and transcription/,
  "es/index.html": /Reproducción, búsqueda y transcripción sobre audio sin pausas/,
  "de/index.html": /Wiedergabe, Suche und Transkription mit entfernten Pausen/,
  "be/index.html": /Прайграванне, пошук і распазнаванне па аўдыя без паўз/
};
for (const relativePage of pages) {
  const pagePath = path.join(siteDirectory, relativePage);
  const html = fs.readFileSync(pagePath, "utf8");
  assert.equal((html.match(/data-carousel-slide/g) || []).length, 4, `${relativePage} must have four slides`);
  assert.equal((html.match(/data-carousel-index/g) || []).length, 4, `${relativePage} must have four slide selectors`);
  assert.match(html, /data-carousel tabindex="0" aria-roledescription="carousel"/, `${relativePage} needs a keyboard-focusable carousel`);
  assert.doesNotMatch(html, /class="app-window"/, `${relativePage} still contains the old mock preview`);
  assert.match(html, /"softwareVersion": "0\.4\.0"/, `${relativePage} has stale release metadata`);
  assert.match(html, /"operatingSystem": \["Windows 10\/11 x64", "macOS 13\+", "Ubuntu 24\.04\+ x64"\]/, `${relativePage} needs all desktop hosts in structured data`);
  assert.match(html, recordingCopy[relativePage], `${relativePage} does not explain pause-cut recording transcription`);

  const linkedAssets = [...new Set(
    [...html.matchAll(/data-download-asset="([^"]+)"/g)].map((match) => match[1]))
  ].sort();
  assert.deepEqual(linkedAssets, [...releaseAssets].sort(), `${relativePage} has incomplete release downloads`);

  const screenshotSources = [...html.matchAll(/<img[^>]+src="([^"]*screenshots\/[^"]+)"/g)].map((match) => match[1]);
  assert.equal(screenshotSources.length, 4, `${relativePage} must reference four screenshots`);
  for (const source of screenshotSources) {
    const imagePath = path.resolve(path.dirname(pagePath), source);
    assert.ok(fs.existsSync(imagePath), `${relativePage} references missing image ${source}`);
    assert.deepEqual(pngDimensions(imagePath), { width: 1284, height: 842 }, `${source} has unexpected dimensions`);
  }
}

{
  const result = runApp({ languages: ["es-MX", "en-US"] });
  assert.deepEqual(result.redirects, [{ kind: "replace", url: "./es/" }]);
  assert.equal(result.storage.get("buddy-language"), "es");
}

{
  const result = runApp({ languages: ["es-ES"], savedLocale: "de" });
  assert.deepEqual(result.redirects, [{ kind: "replace", url: "./de/" }]);
}

{
  const result = runApp({ languages: ["ru-RU"] });
  assert.deepEqual(result.redirects, []);
  assert.equal(result.storage.get("buddy-language"), "en");
}

{
  const result = runApp({ languages: ["ru-RU", "be-BY"] });
  assert.deepEqual(result.redirects, [{ kind: "replace", url: "./be/" }]);
}

{
  const result = runApp({ languages: ["de-DE"], search: "?lang=en" });
  assert.deepEqual(result.redirects, []);
  assert.equal(result.storage.has("buddy-language"), false);
}

{
  const { carouselState } = runApp({ locale: "de", withCarousel: true });
  const { carousel, slides, dots, previous, next, current } = carouselState;
  next.dispatch("click");
  assert.equal(current.textContent, "2");
  assert.equal(slides[1].hidden, false);
  assert.equal(dots[1].attributes.get("aria-current"), "true");
  previous.dispatch("click");
  assert.equal(current.textContent, "1");
  dots[3].dispatch("click");
  assert.equal(current.textContent, "4");
  carousel.dispatch("keydown", { key: "Home", preventDefault() {} });
  assert.equal(current.textContent, "1");
  carousel.dispatch("keydown", { key: "End", preventDefault() {} });
  assert.equal(current.textContent, "4");
  carousel.dispatch("touchstart", { changedTouches: [{ clientX: 200 }] });
  carousel.dispatch("touchend", { changedTouches: [{ clientX: 100 }] });
  assert.equal(current.textContent, "1");
}

console.log("Buddy website validation passed: four localized carousels, desktop tiers, six release assets, recording copy, language routing, controls, and 1284x842 screenshots.");
