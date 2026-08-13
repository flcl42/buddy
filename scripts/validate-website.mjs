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

  add(...names) {
    names.forEach((name) => this.classes.add(name));
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
  const expandTargets = Array.from({ length: 4 }, () => new FakeElement());
  const images = Array.from({ length: 4 }, (_, index) => {
    const image = new FakeElement();
    image.alt = `Screenshot ${index + 1}`;
    image.closest = () => expandTargets[index];
    slides[index].querySelector = (selector) => selector === "img" ? image : null;
    return image;
  });
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

  return { carousel, slides, dots, previous, next, current, expandTargets, images };
}

function runApp({ locale = "en", languages = ["en-US"], savedLocale = "", search = "", route = "", withCarousel = false } = {}) {
  const storage = new Map(savedLocale ? [["buddy-language", savedLocale]] : []);
  const redirects = [];
  const carouselState = withCarousel ? createCarousel() : null;
  const document = {
    body: { dataset: { locale, root: route ? (locale === "en" ? ".." : "../..") : (locale === "en" ? "." : ".."), route } },
    querySelector: () => null,
    querySelectorAll: (selector) => selector === "[data-carousel]" && carouselState ? [carouselState.carousel] : []
  };
  const window = {
    BUDDY_REPOSITORY: "",
    location: {
      hostname: "localhost",
      pathname: locale === "en" ? `/${route}` : `/${locale}/${route}`,
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

const pages = ["index.html", "es/index.html", "de/index.html", "be/index.html", "ru/index.html"];
const guidePages = [
  "deepseek-api-key/index.html",
  "es/deepseek-api-key/index.html",
  "de/deepseek-api-key/index.html",
  "be/deepseek-api-key/index.html",
  "ru/deepseek-api-key/index.html"
];
const privacyPages = [
  "privacy/index.html",
  "es/privacy/index.html",
  "de/privacy/index.html",
  "be/privacy/index.html",
  "ru/privacy/index.html"
];
const releaseAssets = [
  "Buddy-Setup.exe",
  "Buddy.exe",
  "Buddy-macOS-arm64-beta.zip",
  "Buddy-Linux-x64-preview.deb",
  "Buddy-Linux-x64-preview.tar.gz"
];
const recordingCopy = {
  "index.html": /Pause-cut playback, seeking, and transcription/,
  "es/index.html": /Reproducción, búsqueda y transcripción sobre audio sin pausas/,
  "de/index.html": /Wiedergabe, Suche und Transkription mit entfernten Pausen/,
  "be/index.html": /Прайграванне, пошук і распазнаванне па аўдыя без паўз/,
  "ru/index.html": /Воспроизведение, поиск и транскрипция аудио без пауз/
};
const heroCopy = {
  "index.html": {
    title: "Speak better.<br><span>Connect with confidence.</span>",
    description: "Buddy helps you practise conversations, analyse your pronunciation and delivery, refine your vocabulary and grammar, and learn through contextual spoken AI dialogue"
  },
  "es/index.html": {
    title: "Habla mejor.<br><span>Comunícate con confianza.</span>",
    description: "Buddy te ayuda a practicar conversaciones, analizar tu pronunciación y tu forma de expresarte, mejorar tu vocabulario y gramática"
  },
  "de/index.html": {
    title: "Besser sprechen.<br><span>Sicherer kommunizieren.</span>",
    description: "Buddy hilft dir, Gespräche zu üben, Aussprache und Ausdruck zu analysieren, Wortschatz und Grammatik zu verbessern"
  },
  "be/index.html": {
    title: "Гаварыце лепш.<br><span>Размаўляйце ўпэўнена.</span>",
    description: "Buddy дапамагае практыкаваць размовы, аналізаваць вымаўленне і манеру маўлення, удасканальваць слоўнікавы запас і граматыку"
  },
  "ru/index.html": {
    title: "Говорите лучше.<br><span>Общайтесь увереннее.</span>",
    description: "Buddy помогает практиковать общение, анализировать произношение и подачу, улучшать словарный запас и грамматику"
  }
};
const walkthroughVideo = path.join(siteDirectory, "video", "buddy-walkthrough.mp4");
const walkthroughPoster = path.join(siteDirectory, "video", "buddy-walkthrough-poster.jpg");
const walkthroughLocales = ["en", "es", "de", "be", "ru"];
assert.ok(fs.statSync(walkthroughVideo).size > 1_000_000, "walkthrough video is unexpectedly small");
assert.equal(fs.readFileSync(walkthroughVideo).subarray(4, 8).toString("ascii"), "ftyp", "walkthrough is not an MP4");
assert.ok(fs.statSync(walkthroughPoster).size > 50_000, "walkthrough poster is unexpectedly small");
for (const locale of walkthroughLocales) {
  const captions = fs.readFileSync(path.join(siteDirectory, "video", `buddy-walkthrough.${locale}.vtt`), "utf8");
  assert.match(captions, /^WEBVTT\r?\n/, `${locale} captions are not WebVTT`);
  assert.equal((captions.match(/-->/g) || []).length, 5, `${locale} captions need all five spoken passages`);
}
for (const relativePage of pages) {
  const pagePath = path.join(siteDirectory, relativePage);
  const html = fs.readFileSync(pagePath, "utf8");
  assert.equal((html.match(/data-carousel-slide/g) || []).length, 4, `${relativePage} must have four slides`);
  assert.equal((html.match(/data-carousel-index/g) || []).length, 4, `${relativePage} must have four slide selectors`);
  assert.match(html, /data-carousel tabindex="0" aria-roledescription="carousel"/, `${relativePage} needs a keyboard-focusable carousel`);
  assert.doesNotMatch(html, /class="app-window"/, `${relativePage} still contains the old mock preview`);
  assert.match(html, /"softwareVersion": "0\.4\.1"/, `${relativePage} has stale release metadata`);
  assert.match(html, /"operatingSystem": \["Windows 10\/11 x64", "macOS 13\+", "Ubuntu 24\.04\+ x64"\]/, `${relativePage} needs all desktop hosts in structured data`);
  assert.match(html, recordingCopy[relativePage], `${relativePage} does not explain pause-cut recording transcription`);
  assert.ok(html.includes(`<h1>${heroCopy[relativePage].title}</h1>`), `${relativePage} has stale hero positioning`);
  assert.ok(html.includes(heroCopy[relativePage].description), `${relativePage} has stale hero supporting copy`);
  assert.doesNotMatch(html, /Remember your words|Recuerda tus palabras|Erinnere dich an deine Worte|Памятайце свае словы|Помните свои слова/, `${relativePage} retains the discarded recall headline`);
  assert.equal((html.match(/rel="alternate" hreflang=/g) || []).length, 6, `${relativePage} needs complete language alternates`);
  assert.match(html, /<option value="ru"(?: selected)?>Русский<\/option>/, `${relativePage} needs the Russian language choice`);
  assert.equal((html.match(/<video\b/g) || []).length, 1, `${relativePage} must include one walkthrough video`);
  assert.match(html, /<video[^>]+\bcontrols\b[^>]+\bpreload="metadata"/, `${relativePage} needs accessible native video controls`);
  assert.doesNotMatch(html, /<video[^>]+\bautoplay\b/, `${relativePage} must not autoplay the walkthrough`);
  assert.match(html, /"@type": "VideoObject"/, `${relativePage} needs VideoObject structured data`);
  assert.match(html, /"duration": "PT58S"/, `${relativePage} has stale walkthrough duration metadata`);

  const mediaRoot = relativePage === "index.html" ? "./video" : "../video";
  assert.ok(html.includes(`poster="${mediaRoot}/buddy-walkthrough-poster.jpg"`), `${relativePage} has the wrong poster path`);
  assert.ok(html.includes(`<source src="${mediaRoot}/buddy-walkthrough.mp4" type="video/mp4">`), `${relativePage} has the wrong video path`);
  for (const locale of walkthroughLocales) {
    assert.ok(html.includes(`src="${mediaRoot}/buddy-walkthrough.${locale}.vtt"`), `${relativePage} is missing ${locale} captions`);
  }
  assert.equal((html.match(/<track[^>]+\bdefault\b/g) || []).length, 1, `${relativePage} must select exactly one default caption track`);
  assert.match(html, /href="\.\/deepseek-api-key\/"/, `${relativePage} must link to its localized DeepSeek guide`);
  assert.match(html, /href="\.\/privacy\/"/, `${relativePage} must link to its localized privacy policy`);
  assert.match(html, /DeepSeek V4 Flash/, `${relativePage} must identify the hosted dialog model`);
  assert.match(html, /Qwen 3\.6 27B/, `${relativePage} must identify the local dialog model`);
  assert.match(html, /DFlash/, `${relativePage} must explain local-model acceleration`);

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

for (const relativePage of guidePages) {
  const pagePath = path.join(siteDirectory, relativePage);
  const html = fs.readFileSync(pagePath, "utf8");
  assert.match(html, /data-route="deepseek-api-key\/"/, `${relativePage} must retain guide routing across languages`);
  assert.equal((html.match(/class="guide-section(?:\s[^\"]*)?"/g) || []).length, 5, `${relativePage} needs all five setup sections`);
  assert.match(html, /https:\/\/platform\.deepseek\.com\/api_keys/, `${relativePage} needs the official key page`);
  assert.match(html, /https:\/\/platform\.deepseek\.com\/top_up/, `${relativePage} needs the official billing page`);
  assert.match(html, /https:\/\/cdn\.deepseek\.com\/policies\/en-US\/deepseek-privacy-policy\.html/, `${relativePage} needs the current privacy policy`);
  assert.match(html, /https:\/\/cdn\.deepseek\.com\/policies\/en-US\/deepseek-open-platform-terms-of-service\.html/, `${relativePage} needs the Open Platform terms`);
  assert.match(html, /<meta name="robots" content="index, follow, max-image-preview:large">/, `${relativePage} must be indexable`);
  assert.match(html, /"@type": "HowTo"/, `${relativePage} needs HowTo structured data`);
  assert.doesNotMatch(html, /zero data retention[^<]*(enabled|guaranteed)/i, `${relativePage} must not promise zero data retention`);
  assert.equal((html.match(/rel="alternate" hreflang=/g) || []).length, 6, `${relativePage} needs complete language alternates`);
  assert.match(html, /<option value="ru"(?: selected)?>Русский<\/option>/, `${relativePage} needs the Russian language choice`);
  assert.match(html, /href="\.\.\/privacy\/"/, `${relativePage} must link to its localized privacy policy`);

  const documentHeader = html.match(/<header class="site-header document-header">([\s\S]*?)<\/header>/)?.[1];
  assert.ok(documentHeader, `${relativePage} needs the compact document header`);
  assert.doesNotMatch(documentHeader, /class="main-nav"|class="button/, `${relativePage} must not use landing-page navigation or calls to action`);

  const ids = [...html.matchAll(/\sid="([^"]+)"/g)].map((match) => match[1]);
  assert.equal(new Set(ids).size, ids.length, `${relativePage} has duplicate element IDs`);

  const externalTabs = [...html.matchAll(/<a\b[^>]*\btarget="_blank"[^>]*>/g)].map((match) => match[0]);
  assert.ok(externalTabs.length >= 7, `${relativePage} is missing official external actions`);
  externalTabs.forEach((tag) => {
    assert.match(tag, /\brel="[^"]*noopener[^"]*noreferrer[^"]*"/, `${relativePage} has an unsafe external tab`);
  });

  const localResources = [...html.matchAll(/\b(?:href|src)="((?:\.\.\/|\.\/)[^"]*)"/g)]
    .map((match) => match[1].split(/[?#]/, 1)[0])
    .filter(Boolean);
  localResources.forEach((resource) => {
    assert.ok(fs.existsSync(path.resolve(path.dirname(pagePath), resource)), `${relativePage} references missing local resource ${resource}`);
  });
}

for (const relativePage of privacyPages) {
  const pagePath = path.join(siteDirectory, relativePage);
  const html = fs.readFileSync(pagePath, "utf8");
  assert.match(html, /data-route="privacy\/"/, `${relativePage} must retain privacy routing across languages`);
  assert.equal((html.match(/class="guide-section(?:\s[^"]*)?"/g) || []).length, 12, `${relativePage} needs all twelve policy sections`);
  assert.match(html, /Aliaksei Osipau/, `${relativePage} needs the responsible person's current name`);
  assert.ok((html.match(/buddy@flcl\.me/g) || []).length >= 3, `${relativePage} needs the privacy contact address`);
  assert.doesNotMatch(html, /Alexey Osipov|me@flcl\.me/, `${relativePage} contains stale contact details`);
  assert.match(html, /https:\/\/cdn\.deepseek\.com\/policies\/en-US\/deepseek-privacy-policy\.html/, `${relativePage} needs DeepSeek's privacy policy`);
  assert.match(html, /https:\/\/cdn\.deepseek\.com\/policies\/en-US\/deepseek-open-platform-terms-of-service\.html/, `${relativePage} needs DeepSeek's platform terms`);
  assert.match(html, /https:\/\/telegram\.org\/privacy/, `${relativePage} needs Telegram's privacy policy`);
  assert.match(html, /https:\/\/huggingface\.co\/privacy/, `${relativePage} needs Hugging Face's privacy policy`);
  assert.match(html, /https:\/\/docs\.github\.com\/en\/site-policy\/privacy-policies\/github-general-privacy-statement/, `${relativePage} needs GitHub's privacy statement`);
  assert.match(html, /<meta name="robots" content="index, follow, max-image-preview:large">/, `${relativePage} must be indexable`);
  assert.match(html, /"@type":"?\s*"?WebPage"?/, `${relativePage} needs WebPage structured data`);
  assert.equal((html.match(/rel="alternate" hreflang=/g) || []).length, 6, `${relativePage} needs complete language alternates`);
  assert.match(html, /<option value="ru"(?: selected)?>Русский<\/option>/, `${relativePage} needs the Russian language choice`);

  const documentHeader = html.match(/<header class="site-header document-header">([\s\S]*?)<\/header>/)?.[1];
  assert.ok(documentHeader, `${relativePage} needs the compact document header`);
  assert.doesNotMatch(documentHeader, /class="main-nav"|class="button/, `${relativePage} must not use landing-page navigation or calls to action`);

  const ids = [...html.matchAll(/\sid="([^"]+)"/g)].map((match) => match[1]);
  assert.equal(new Set(ids).size, ids.length, `${relativePage} has duplicate element IDs`);

  const externalTabs = [...html.matchAll(/<a\b[^>]*\btarget="_blank"[^>]*>/g)].map((match) => match[0]);
  assert.ok(externalTabs.length >= 5, `${relativePage} is missing third-party policy links`);
  externalTabs.forEach((tag) => {
    assert.match(tag, /\brel="[^"]*noopener[^"]*noreferrer[^"]*"/, `${relativePage} has an unsafe external tab`);
  });

  const localResources = [...html.matchAll(/\b(?:href|src)="((?:\.\.\/|\.\/)[^"]*)"/g)]
    .map((match) => match[1].split(/[?#]/, 1)[0])
    .filter(Boolean);
  localResources.forEach((resource) => {
    assert.ok(fs.existsSync(path.resolve(path.dirname(pagePath), resource)), `${relativePage} references missing local resource ${resource}`);
  });
}

{
  const sitemap = fs.readFileSync(path.join(siteDirectory, "sitemap.xml"), "utf8");
  for (const route of [
    "ru/",
    "ru/deepseek-api-key/",
    "privacy/",
    "es/privacy/",
    "de/privacy/",
    "be/privacy/",
    "ru/privacy/"
  ]) {
    assert.ok(sitemap.includes(`__SITE_BASE_URL__/${route}`), `sitemap is missing ${route}`);
  }
}

assert.deepEqual(
  pngDimensions(path.join(siteDirectory, "og.png")),
  { width: 1200, height: 630 },
  "social preview must remain 1200×630");

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
  assert.deepEqual(result.redirects, [{ kind: "replace", url: "./ru/" }]);
  assert.equal(result.storage.get("buddy-language"), "ru");
}

{
  const result = runApp({ languages: ["ru-RU", "be-BY"] });
  assert.deepEqual(result.redirects, [{ kind: "replace", url: "./ru/" }]);
}

{
  const result = runApp({ languages: ["be-BY", "ru-RU"] });
  assert.deepEqual(result.redirects, [{ kind: "replace", url: "./be/" }]);
}

{
  const result = runApp({ languages: ["de-DE"], search: "?lang=en" });
  assert.deepEqual(result.redirects, []);
  assert.equal(result.storage.has("buddy-language"), false);
}

{
  const result = runApp({ languages: ["es-ES"], route: "deepseek-api-key/" });
  assert.deepEqual(result.redirects, [{ kind: "replace", url: "../es/deepseek-api-key/" }]);
}

{
  const result = runApp({ languages: ["de-DE"], route: "privacy/" });
  assert.deepEqual(result.redirects, [{ kind: "replace", url: "../de/privacy/" }]);
}

{
  const result = runApp({ locale: "be", languages: ["be-BY"], savedLocale: "be", route: "privacy/" });
  assert.deepEqual(result.redirects, []);
}

{
  const result = runApp({ locale: "de", languages: ["de-DE"], savedLocale: "de", route: "deepseek-api-key/" });
  assert.deepEqual(result.redirects, []);
}

{
  const result = runApp({ locale: "ru", languages: ["ru-RU"], savedLocale: "ru", route: "privacy/" });
  assert.deepEqual(result.redirects, []);
}

{
  const { carouselState } = runApp({ locale: "de", withCarousel: true });
  const { carousel, slides, dots, previous, next, current, expandTargets } = carouselState;
  expandTargets.forEach((target) => {
    assert.equal(target.classList.contains("carousel-expand-target"), true);
    assert.equal(target.attributes.get("role"), "button");
    assert.equal(target.attributes.get("tabindex"), "0");
    assert.match(target.attributes.get("aria-label"), /^Screenshot vergrößert öffnen:/);
    assert.equal((target.listeners.get("click") || []).length, 1);
    assert.equal((target.listeners.get("keydown") || []).length, 1);
  });
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

assert.match(appSource, /dialog\.showModal\(\)/, "carousel must use a modal image viewer");
assert.match(appSource, /data-lightbox-previous/, "enlarged viewer needs a previous-image control");
assert.match(appSource, /data-lightbox-next/, "enlarged viewer needs a next-image control");
assert.match(appSource, /previous\.addEventListener\("click", \(\) => showImage\(activeIndex - 1\)\)/, "previous-image control must navigate without closing the viewer");
assert.match(appSource, /next\.addEventListener\("click", \(\) => showImage\(activeIndex \+ 1\)\)/, "next-image control must navigate without closing the viewer");
assert.match(appSource, /event\.key === "ArrowLeft"[\s\S]*showImage\(activeIndex - 1\)/, "enlarged viewer must support the left arrow key");
assert.match(appSource, /event\.key === "ArrowRight"[\s\S]*showImage\(activeIndex \+ 1\)/, "enlarged viewer must support the right arrow key");
assert.doesNotMatch(appSource, /data-lightbox-zoom|nextZoom|setZoom/, "enlarged viewer must not retain zoom controls");

console.log("Buddy website validation passed: five localized product, DeepSeek key-guide, and privacy-policy pages, Russian routing and captions, release assets, a zoom-control-free enlarged carousel viewer with side navigation, screenshots, and an accessible walkthrough video.");
