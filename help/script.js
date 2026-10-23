(function () {
  "use strict";

  function activate(list, target) {
    list.forEach(function (el) {
      el.classList.toggle("active", el === target);
    });
  }

  function showSection(id) {
    var panels = document.querySelectorAll(".panel");
    var menuButtons = document.querySelectorAll(".menustrip button");
    var sideLinks = document.querySelectorAll(".side-list a");

    var targetPanel = document.getElementById("panel-" + id);
    if (!targetPanel) return;

    activate(Array.prototype.slice.call(panels), targetPanel);
    activate(Array.prototype.slice.call(menuButtons), document.querySelector('.menustrip button[data-target="' + id + '"]'));
    activate(Array.prototype.slice.call(sideLinks), document.querySelector('.side-list a[data-target="' + id + '"]'));

    // Switching sections always shows the first sub-tab
    var firstSubtab = targetPanel.querySelector(".subtabs button");
    if (firstSubtab) {
      showSubtab(targetPanel, firstSubtab.dataset.sub);
    }

    window.scrollTo(0, 0);
    history.replaceState(null, "", "#" + id);
  }

  function showSubtab(panel, subId) {
    var buttons = panel.querySelectorAll(".subtabs button");
    var subpanels = panel.querySelectorAll(".subpanel");

    activate(Array.prototype.slice.call(buttons), panel.querySelector('.subtabs button[data-sub="' + subId + '"]'));
    activate(Array.prototype.slice.call(subpanels), panel.querySelector('.subpanel[data-sub="' + subId + '"]'));
  }

  document.addEventListener("click", function (e) {
    var menuBtn = e.target.closest(".menustrip button[data-target]");
    if (menuBtn) {
      showSection(menuBtn.dataset.target);
      return;
    }

    var sideLink = e.target.closest(".side-list a[data-target]");
    if (sideLink) {
      e.preventDefault();
      showSection(sideLink.dataset.target);
      return;
    }

    var subBtn = e.target.closest(".subtabs button[data-sub]");
    if (subBtn) {
      var panel = subBtn.closest(".panel");
      showSubtab(panel, subBtn.dataset.sub);
      return;
    }

    var jump = e.target.closest("a[data-jump]");
    if (jump) {
      e.preventDefault();
      var parts = jump.dataset.jump.split(":");
      showSection(parts[0]);
      if (parts[1]) {
        var panel = document.getElementById("panel-" + parts[0]);
        showSubtab(panel, parts[1]);
      }
    }
  });

  var initial = (location.hash || "").replace("#", "") || "start";
  if (!document.getElementById("panel-" + initial)) {
    initial = "start";
  }
  showSection(initial);

  // Load the current version live from GitHub instead of maintaining it here
  var versionBadge = document.getElementById("version-badge");
  var downloadVersion = document.getElementById("download-version");
  if (versionBadge || downloadVersion) {
    fetch("https://api.github.com/repos/transfairs/A2G-Scaler/releases/latest")
      .then(function (res) { if (!res.ok) throw new Error(res.status); return res.json(); })
      .then(function (release) {
        if (versionBadge) {
          versionBadge.textContent = "🏷️ " + release.tag_name;
          versionBadge.href = release.html_url;
        }
        if (downloadVersion) {
          downloadVersion.textContent = release.tag_name;
        }
      })
      .catch(function () {
        if (versionBadge) {
          versionBadge.textContent = "Releases";
          versionBadge.href = "https://github.com/transfairs/A2G-Scaler/releases/latest";
        }
        if (downloadVersion) {
          downloadVersion.style.display = "none";
        }
      });
  }

  // Build the full changelog live from all GitHub releases.
  // Release notes may contain more than "- line" bullets (headings, "*" lists,
  // bold, links, "---"), hence a small line-based Markdown renderer instead of
  // a plain bullet parser.
  var changelogList = document.getElementById("changelog-list");
  if (changelogList) {
    function escapeHtml(text) {
      var div = document.createElement("div");
      div.textContent = text == null ? "" : text;
      // also escape quotes, since the text may end up in href="..."
      return div.innerHTML.replace(/"/g, "&quot;");
    }

    function formatDate(iso) {
      var d = new Date(iso);
      if (isNaN(d.getTime())) return "";
      return d.toLocaleDateString("de-DE", { year: "numeric", month: "long", day: "numeric" });
    }

    function renderInline(text) {
      var html = escapeHtml(text);
      html = html.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
      html = html.replace(/`([^`]+)`/g, "<code>$1</code>");
      html = html.replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>');
      html = html.replace(/(^|[\s(])(https?:\/\/[^\s<)]+)/g, '$1<a href="$2" target="_blank" rel="noopener">$2</a>');
      return html;
    }

    function renderBody(body) {
      var lines = (body || "").split(/\r?\n/);
      var html = "";
      var listOpen = false;
      function closeList() { if (listOpen) { html += "</ul>"; listOpen = false; } }

      lines.forEach(function (raw) {
        var line = raw.trim();

        if (line === "") { closeList(); return; }

        var heading = line.match(/^(#{1,6})\s+(.+)$/);
        if (heading) {
          closeList();
          var level = Math.min(heading[1].length + 2, 6);
          html += "<h" + level + ">" + renderInline(heading[2]) + "</h" + level + ">";
          return;
        }

        if (/^(-{3,}|\*{3,}|_{3,})$/.test(line)) {
          closeList();
          html += "<hr>";
          return;
        }

        var item = line.match(/^[-*]\s+(.+)$/);
        if (item) {
          if (!listOpen) { html += "<ul>"; listOpen = true; }
          html += "<li>" + renderInline(item[1]) + "</li>";
          return;
        }

        closeList();
        html += "<p>" + renderInline(line) + "</p>";
      });

      closeList();
      return html || '<p class="changelog-status"><em>Keine Release-Notes vorhanden.</em></p>';
    }

    function renderRelease(release) {
      var tag = escapeHtml(release.tag_name || release.name || "");
      var date = formatDate(release.published_at || release.created_at);
      return (
        '<div class="changelog-entry">' +
          "<h2>" +
            '<a class="tag" href="' + escapeHtml(release.html_url) + '" target="_blank" rel="noopener">' + tag + "</a>" +
            '<span class="date">' + date + "</span>" +
          "</h2>" +
          renderBody(release.body) +
        "</div>"
      );
    }

    fetch("https://api.github.com/repos/transfairs/A2G-Scaler/releases?per_page=100")
      .then(function (res) { if (!res.ok) throw new Error(res.status); return res.json(); })
      .then(function (releases) {
        if (!releases.length) {
          changelogList.innerHTML = '<p class="changelog-status">Noch keine Releases veröffentlicht.</p>';
          return;
        }
        changelogList.innerHTML = releases.map(renderRelease).join("");
      })
      .catch(function () {
        changelogList.innerHTML = '<p class="changelog-status">Changelog konnte nicht geladen werden. Vollständige Historie auf <a href="https://github.com/transfairs/A2G-Scaler/releases" target="_blank" rel="noopener">GitHub</a>.</p>';
      });
  }
})();
