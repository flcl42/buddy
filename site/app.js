(() => {
  "use strict";

  const body = document.body;
  const locale = body.dataset.locale || "en";
  const root = body.dataset.root || ".";
  const supportedLocales = new Set(["en", "es", "de", "be"]);
  const copy = {
    en: {
      latest: "Windows stable · macOS beta · Linux preview",
      release: (tag, date) => `${tag} · Windows stable · macOS beta · Linux preview${date ? ` · ${date}` : ""}`,
      pending: "Desktop builds · First public release coming soon",
      pendingNote: "The first download is being prepared. Follow the GitHub releases page for availability.",
      incompleteNote: "The latest release is published, but one or more expected platform files are not attached yet. Open the release notes for details.",
      unavailable: "Desktop builds · Open GitHub for release availability"
    },
    es: {
      latest: "Windows estable · macOS beta · Linux preliminar",
      release: (tag, date) => `${tag} · Windows estable · macOS beta · Linux preliminar${date ? ` · ${date}` : ""}`,
      pending: "Versiones de escritorio · La primera versión pública llegará pronto",
      pendingNote: "La primera descarga está en preparación. Consulta la página de versiones de GitHub para saber cuándo estará disponible.",
      incompleteNote: "La última versión está publicada, pero aún faltan uno o más archivos esperados para las plataformas. Consulta las notas de la versión.",
      unavailable: "Versiones de escritorio · Consulta la disponibilidad en GitHub"
    },
    de: {
      latest: "Windows stabil · macOS Beta · Linux Vorschau",
      release: (tag, date) => `${tag} · Windows stabil · macOS Beta · Linux Vorschau${date ? ` · ${date}` : ""}`,
      pending: "Desktop-Builds · Erste öffentliche Version folgt bald",
      pendingNote: "Der erste Download wird vorbereitet. Den aktuellen Stand findest du auf der GitHub-Releases-Seite.",
      incompleteNote: "Die neueste Version ist veröffentlicht, aber mindestens eine erwartete Plattformdatei fehlt noch. Einzelheiten stehen in den Versionshinweisen.",
      unavailable: "Desktop-Builds · Verfügbarkeit auf GitHub prüfen"
    },
    be: {
      latest: "Windows стабільная · macOS бэта · Linux папярэдняя",
      release: (tag, date) => `${tag} · Windows стабільная · macOS бэта · Linux папярэдняя${date ? ` · ${date}` : ""}`,
      pending: "Настольныя зборкі · Першы публічны выпуск неўзабаве",
      pendingNote: "Першая спампоўка рыхтуецца. Сачыце за даступнасцю на старонцы выпускаў GitHub.",
      incompleteNote: "Апошні выпуск ужо апублікаваны, але адзін або некалькі чаканых файлаў для платформ яшчэ не далучаны. Падрабязнасці ёсць у заўвагах да выпуску.",
      unavailable: "Настольныя зборкі · Праверце даступнасць на GitHub"
    }
  }[locale];

  function localeUrl(nextLocale) {
    return nextLocale === "en" ? `${root}/` : `${root}/${nextLocale}/`;
  }

  function normalizedLocale(localeTag) {
    return String(localeTag || "").trim().toLowerCase().split(/[-_]/, 1)[0];
  }

  function browserLocale() {
    const preferredLanguages = Array.from(navigator.languages || []);
    const candidates = [...preferredLanguages, navigator.language];
    for (const candidate of candidates) {
      const normalized = normalizedLocale(candidate);
      if (supportedLocales.has(normalized)) {
        return normalized;
      }
    }

    return "en";
  }

  const languageSelect = document.querySelector("#language-select");
  if (languageSelect) {
    languageSelect.value = locale;
    languageSelect.addEventListener("change", () => {
      const nextLocale = languageSelect.value;
      if (!supportedLocales.has(nextLocale)) {
        return;
      }

      try {
        localStorage.setItem("buddy-language", nextLocale);
      } catch {
        // Language selection still works when storage is unavailable.
      }

      window.location.assign(localeUrl(nextLocale));
    });
  }

  if (locale === "en") {
    let savedLocale = "";
    try {
      savedLocale = localStorage.getItem("buddy-language") || "";
    } catch {
      // Keep English when storage is unavailable.
    }

    if (!supportedLocales.has(savedLocale)) {
      savedLocale = "";
    }

    if (!new URLSearchParams(window.location.search).has("lang")) {
      const firstVisitLocale = supportedLocales.has(savedLocale) ? savedLocale : browserLocale();

      if (!savedLocale) {
        try {
          localStorage.setItem("buddy-language", firstVisitLocale);
        } catch {
          // Detection remains useful for this visit when storage is unavailable.
        }
      }

      if (firstVisitLocale !== "en") {
        window.location.replace(localeUrl(firstVisitLocale));
        return;
      }
    }
  }

  document.querySelectorAll("[data-carousel]").forEach((carousel) => {
    const slides = Array.from(carousel.querySelectorAll("[data-carousel-slide]"));
    const dots = Array.from(carousel.querySelectorAll("[data-carousel-index]"));
    const previous = carousel.querySelector("[data-carousel-prev]");
    const next = carousel.querySelector("[data-carousel-next]");
    const current = carousel.querySelector("[data-carousel-current]");
    let activeIndex = Math.max(0, slides.findIndex((slide) => slide.classList.contains("is-active")));
    let touchStartX = null;

    if (!slides.length) {
      return;
    }

    function showSlide(requestedIndex) {
      activeIndex = (requestedIndex + slides.length) % slides.length;
      slides.forEach((slide, index) => {
        const isActive = index === activeIndex;
        slide.hidden = !isActive;
        slide.classList.toggle("is-active", isActive);
      });
      dots.forEach((dot, index) => {
        const isActive = index === activeIndex;
        dot.classList.toggle("is-active", isActive);
        if (isActive) {
          dot.setAttribute("aria-current", "true");
        } else {
          dot.removeAttribute("aria-current");
        }
      });
      if (current) {
        current.textContent = String(activeIndex + 1);
      }
    }

    previous?.addEventListener("click", () => showSlide(activeIndex - 1));
    next?.addEventListener("click", () => showSlide(activeIndex + 1));
    dots.forEach((dot) => {
      dot.addEventListener("click", () => showSlide(Number(dot.dataset.carouselIndex)));
    });

    carousel.addEventListener("keydown", (event) => {
      if (event.key === "ArrowLeft") {
        event.preventDefault();
        showSlide(activeIndex - 1);
      } else if (event.key === "ArrowRight") {
        event.preventDefault();
        showSlide(activeIndex + 1);
      } else if (event.key === "Home") {
        event.preventDefault();
        showSlide(0);
      } else if (event.key === "End") {
        event.preventDefault();
        showSlide(slides.length - 1);
      }
    });

    carousel.addEventListener("touchstart", (event) => {
      touchStartX = event.changedTouches[0]?.clientX ?? null;
    }, { passive: true });
    carousel.addEventListener("touchend", (event) => {
      if (touchStartX === null) {
        return;
      }

      const deltaX = (event.changedTouches[0]?.clientX ?? touchStartX) - touchStartX;
      touchStartX = null;
      if (Math.abs(deltaX) >= 48) {
        showSlide(activeIndex + (deltaX < 0 ? 1 : -1));
      }
    }, { passive: true });

    showSlide(activeIndex);
  });

  function inferRepository() {
    const hostname = window.location.hostname.toLowerCase();
    if (!hostname.endsWith(".github.io")) {
      return "";
    }

    const owner = hostname.slice(0, -".github.io".length);
    const firstPathSegment = window.location.pathname.split("/").filter(Boolean)[0];
    const repository = firstPathSegment || `${owner}.github.io`;
    return `${owner}/${repository}`;
  }

  const configuredRepository = String(window.BUDDY_REPOSITORY || "").trim();
  const repository = configuredRepository || inferRepository();
  const validRepository = /^[a-z0-9_.-]+\/[a-z0-9_.-]+$/i.test(repository);
  const status = document.querySelector("#release-status");
  const releaseNote = document.querySelector("[data-release-note]");

  if (!validRepository) {
    if (status) {
      status.textContent = copy.unavailable;
    }
    return;
  }

  const releasePage = `https://github.com/${repository}/releases/latest`;
  const downloadBase = `${releasePage}/download`;

  document.querySelectorAll("[data-release-link]").forEach((link) => {
    link.href = releasePage;
    link.rel = "noopener noreferrer";
  });

  document.querySelectorAll("[data-download-asset]").forEach((link) => {
    const asset = link.dataset.downloadAsset;
    link.href = `${downloadBase}/${encodeURIComponent(asset)}`;
    link.rel = "noopener noreferrer";
  });

  if (status) {
    status.textContent = copy.latest;
  }

  fetch(`https://api.github.com/repos/${repository}/releases/latest`, {
    headers: { Accept: "application/vnd.github+json" }
  })
    .then((response) => {
      if (response.status === 404) {
        document.querySelectorAll("[data-release-link]").forEach((link) => {
          link.href = `https://github.com/${repository}/releases`;
        });
        document.querySelectorAll("[data-download-asset]").forEach((link) => {
          link.href = `https://github.com/${repository}/releases`;
        });
        if (status) {
          status.textContent = copy.pending;
        }
        if (releaseNote) {
          releaseNote.textContent = copy.pendingNote;
        }
        return null;
      }

      return response.ok ? response.json() : null;
    })
    .then((release) => {
      if (!release) {
        return;
      }

      const releaseDate = release.published_at
        ? new Intl.DateTimeFormat(locale, { year: "numeric", month: "short", day: "numeric" }).format(new Date(release.published_at))
        : "";

      if (status) {
        status.textContent = copy.release(release.tag_name || "Latest", releaseDate);
      }

      const assets = new Map((release.assets || []).map((asset) => [asset.name, asset.browser_download_url]));
      const releaseDetails = release.html_url || releasePage;
      let hasMissingAsset = false;
      document.querySelectorAll("[data-release-link]").forEach((link) => {
        link.href = releaseDetails;
      });
      document.querySelectorAll("[data-download-asset]").forEach((link) => {
        const exactAssetUrl = assets.get(link.dataset.downloadAsset);
        if (exactAssetUrl) {
          link.href = exactAssetUrl;
        } else {
          link.href = releaseDetails;
          hasMissingAsset = true;
        }
      });

      if (hasMissingAsset && releaseNote) {
        releaseNote.textContent = copy.incompleteNote;
      }
    })
    .catch(() => {
      // Stable latest-release links remain usable if metadata cannot be loaded.
    });
})();
