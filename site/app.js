(() => {
  "use strict";

  const body = document.body;
  const locale = body.dataset.locale || "en";
  const root = body.dataset.root || ".";
  const supportedLocales = new Set(["en", "es", "de", "be"]);
  const copy = {
    en: {
      latest: "Windows 11 x64 · Latest GitHub release",
      release: (tag, date) => `Windows 11 x64 · ${tag}${date ? ` · ${date}` : ""}`,
      pending: "Windows 11 x64 · First public release coming soon",
      pendingNote: "The first download is being prepared. Follow the GitHub releases page for availability.",
      unavailable: "Windows 11 x64 · Open GitHub for release availability"
    },
    es: {
      latest: "Windows 11 x64 · Última versión de GitHub",
      release: (tag, date) => `Windows 11 x64 · ${tag}${date ? ` · ${date}` : ""}`,
      pending: "Windows 11 x64 · La primera versión pública llegará pronto",
      pendingNote: "La primera descarga está en preparación. Consulta la página de versiones de GitHub para saber cuándo estará disponible.",
      unavailable: "Windows 11 x64 · Consulta la disponibilidad en GitHub"
    },
    de: {
      latest: "Windows 11 x64 · Neueste GitHub-Version",
      release: (tag, date) => `Windows 11 x64 · ${tag}${date ? ` · ${date}` : ""}`,
      pending: "Windows 11 x64 · Erste öffentliche Version folgt bald",
      pendingNote: "Der erste Download wird vorbereitet. Den aktuellen Stand findest du auf der GitHub-Releases-Seite.",
      unavailable: "Windows 11 x64 · Verfügbarkeit auf GitHub prüfen"
    },
    be: {
      latest: "Windows 11 x64 · Апошні выпуск на GitHub",
      release: (tag, date) => `Windows 11 x64 · ${tag}${date ? ` · ${date}` : ""}`,
      pending: "Windows 11 x64 · Першы публічны выпуск неўзабаве",
      pendingNote: "Першая спампоўка рыхтуецца. Сачыце за даступнасцю на старонцы выпускаў GitHub.",
      unavailable: "Windows 11 x64 · Праверце даступнасць на GitHub"
    }
  }[locale];

  function localeUrl(nextLocale) {
    return nextLocale === "en" ? `${root}/` : `${root}/${nextLocale}/`;
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

    if (
      savedLocale !== "en" &&
      supportedLocales.has(savedLocale) &&
      !new URLSearchParams(window.location.search).has("lang")
    ) {
      window.location.replace(localeUrl(savedLocale));
      return;
    }
  }

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
      document.querySelectorAll("[data-download-asset]").forEach((link) => {
        const exactAssetUrl = assets.get(link.dataset.downloadAsset);
        if (exactAssetUrl) {
          link.href = exactAssetUrl;
        }
      });
    })
    .catch(() => {
      // Stable latest-release links remain usable if metadata cannot be loaded.
    });
})();
