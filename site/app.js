(() => {
  "use strict";

  const body = document.body;
  const locale = body.dataset.locale || "en";
  const root = body.dataset.root || ".";
  const route = body.dataset.route || "";
  const supportedLocales = new Set(["en", "es", "de", "be", "ru"]);
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
    },
    ru: {
      latest: "Windows стабильная · macOS бета · Linux предварительная",
      release: (tag, date) => `${tag} · Windows стабильная · macOS бета · Linux предварительная${date ? ` · ${date}` : ""}`,
      pending: "Версии для компьютера · Первый публичный выпуск скоро",
      pendingNote: "Первая загрузка готовится. Следите за доступностью на странице выпусков GitHub.",
      incompleteNote: "Последняя версия опубликована, но одного или нескольких ожидаемых файлов для платформ пока нет. Подробности — в примечаниях к выпуску.",
      unavailable: "Версии для компьютера · Проверьте доступность на GitHub"
    }
  }[locale];

  const lightboxCopy = {
    en: { open: "Open larger screenshot", close: "Close image", previous: "Previous screenshot", next: "Next screenshot", dialog: "Enlarged Buddy screenshot" },
    es: { open: "Abrir captura ampliada", close: "Cerrar imagen", previous: "Captura anterior", next: "Captura siguiente", dialog: "Captura ampliada de Buddy" },
    de: { open: "Screenshot vergrößert öffnen", close: "Bild schließen", previous: "Vorheriger Screenshot", next: "Nächster Screenshot", dialog: "Vergrößerter Buddy-Screenshot" },
    be: { open: "Адкрыць павялічаны здымак", close: "Закрыць выяву", previous: "Папярэдні здымак", next: "Наступны здымак", dialog: "Павялічаны здымак Buddy" },
    ru: { open: "Открыть увеличенный снимок", close: "Закрыть изображение", previous: "Предыдущий снимок", next: "Следующий снимок", dialog: "Увеличенный снимок Buddy" }
  }[locale] || null;

  function localeUrl(nextLocale) {
    return nextLocale === "en"
      ? `${root}/${route}`
      : `${root}/${nextLocale}/${route}`;
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

  let lightbox = null;

  function ensureLightbox() {
    if (lightbox || !lightboxCopy || typeof document.createElement !== "function") {
      return lightbox;
    }

    const dialog = document.createElement("dialog");
    dialog.className = "screenshot-lightbox";
    dialog.setAttribute("aria-label", lightboxCopy.dialog);
    dialog.innerHTML = `
      <div class="lightbox-panel">
        <div class="lightbox-toolbar">
          <span class="lightbox-title"></span>
          <span class="lightbox-count" data-lightbox-count aria-live="polite"></span>
          <button class="lightbox-close" type="button" data-lightbox-close aria-label="${lightboxCopy.close}"><span aria-hidden="true">×</span></button>
        </div>
        <div class="lightbox-stage">
          <button class="lightbox-nav lightbox-previous" type="button" data-lightbox-previous aria-label="${lightboxCopy.previous}"><span aria-hidden="true">←</span></button>
          <div class="lightbox-image-frame"><img alt=""></div>
          <button class="lightbox-nav lightbox-next" type="button" data-lightbox-next aria-label="${lightboxCopy.next}"><span aria-hidden="true">→</span></button>
        </div>
      </div>`;
    document.body.appendChild(dialog);

    const stage = dialog.querySelector(".lightbox-stage");
    const imageFrame = stage.querySelector(".lightbox-image-frame");
    const image = imageFrame.querySelector("img");
    const title = dialog.querySelector(".lightbox-title");
    const count = dialog.querySelector("[data-lightbox-count]");
    const previous = dialog.querySelector("[data-lightbox-previous]");
    const next = dialog.querySelector("[data-lightbox-next]");
    let images = [];
    let activeIndex = 0;
    let onChange = null;

    function renderImage() {
      if (!image.naturalWidth || !image.naturalHeight) {
        return;
      }

      const horizontalReserve = stage.clientWidth <= 720 ? 24 : 144;
      const fitWidth = Math.max(1, stage.clientWidth - horizontalReserve) / image.naturalWidth;
      const fitHeight = Math.max(1, stage.clientHeight - 32) / image.naturalHeight;
      const fit = Math.min(1, fitWidth, fitHeight);
      imageFrame.style.width = `${Math.round(image.naturalWidth * fit)}px`;
      imageFrame.style.height = `${Math.round(image.naturalHeight * fit)}px`;
    }

    function showImage(requestedIndex) {
      if (!images.length) {
        return;
      }

      activeIndex = (requestedIndex + images.length) % images.length;
      const sourceImage = images[activeIndex];
      image.alt = sourceImage.alt || "";
      title.textContent = sourceImage.alt || lightboxCopy.dialog;
      count.textContent = `${activeIndex + 1} / ${images.length}`;
      imageFrame.classList.toggle("screenshot-watermark-mask", Boolean(sourceImage.closest?.(".screenshot-watermark-mask")));
      image.onload = renderImage;
      image.src = sourceImage.currentSrc || sourceImage.src;
      const hasMultipleImages = images.length > 1;
      previous.hidden = !hasMultipleImages;
      next.hidden = !hasMultipleImages;
      onChange?.(activeIndex);
    }

    dialog.querySelector("[data-lightbox-close]").addEventListener("click", () => dialog.close());
    previous.addEventListener("click", () => showImage(activeIndex - 1));
    next.addEventListener("click", () => showImage(activeIndex + 1));
    dialog.addEventListener("keydown", (event) => {
      if (event.key === "ArrowLeft") {
        event.preventDefault();
        showImage(activeIndex - 1);
      } else if (event.key === "ArrowRight") {
        event.preventDefault();
        showImage(activeIndex + 1);
      } else if (event.key === "Home") {
        event.preventDefault();
        showImage(0);
      } else if (event.key === "End") {
        event.preventDefault();
        showImage(images.length - 1);
      }
    });
    dialog.addEventListener("click", (event) => {
      if (event.target === dialog) {
        dialog.close();
      }
    });
    dialog.addEventListener("close", () => {
      image.onload = null;
      image.removeAttribute("src");
      imageFrame.style.removeProperty("width");
      imageFrame.style.removeProperty("height");
      imageFrame.classList.remove("screenshot-watermark-mask");
      images = [];
      activeIndex = 0;
      onChange = null;
    });
    window.addEventListener?.("resize", renderImage);

    lightbox = {
      open(sourceImages, requestedIndex, changeHandler) {
        images = sourceImages;
        onChange = changeHandler;
        dialog.showModal();
        showImage(requestedIndex);
      }
    };
    return lightbox;
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

    const carouselImages = slides.map((slide) => slide.querySelector?.("img")).filter(Boolean);
    slides.forEach((slide, index) => {
      const image = slide.querySelector?.("img");
      if (!image) {
        return;
      }

      let target = image.closest?.(".screenshot-image");
      if (!target && image.parentNode && typeof document.createElement === "function") {
        target = document.createElement("div");
        target.className = "screenshot-image";
        image.parentNode.insertBefore(target, image);
        target.appendChild(image);
      }
      target ||= image;
      target.classList.add("carousel-expand-target");
      target.setAttribute("role", "button");
      target.setAttribute("tabindex", "0");
      target.setAttribute("aria-label", `${lightboxCopy.open}: ${image.alt}`);
      target.addEventListener("click", () => ensureLightbox()?.open(carouselImages, index, showSlide));
      target.addEventListener("keydown", (event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          ensureLightbox()?.open(carouselImages, index, showSlide);
        }
      });
    });

    function showSlide(requestedIndex, focusZoomTarget = false) {
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
      if (focusZoomTarget) {
        slides[activeIndex].querySelector?.(".carousel-expand-target")?.focus();
      }
    }

    previous?.addEventListener("click", () => showSlide(activeIndex - 1));
    next?.addEventListener("click", () => showSlide(activeIndex + 1));
    dots.forEach((dot) => {
      dot.addEventListener("click", () => showSlide(Number(dot.dataset.carouselIndex)));
    });

    carousel.addEventListener("keydown", (event) => {
      const focusZoomTarget = Boolean(event.target?.closest?.(".carousel-expand-target"));
      if (event.key === "ArrowLeft") {
        event.preventDefault();
        showSlide(activeIndex - 1, focusZoomTarget);
      } else if (event.key === "ArrowRight") {
        event.preventDefault();
        showSlide(activeIndex + 1, focusZoomTarget);
      } else if (event.key === "Home") {
        event.preventDefault();
        showSlide(0, focusZoomTarget);
      } else if (event.key === "End") {
        event.preventDefault();
        showSlide(slides.length - 1, focusZoomTarget);
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
