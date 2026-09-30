// Interactive family graph (family-chart on d3). The page only renders an empty [data-family-graph]
// container; this script loads the libraries on demand, fetches a bounded slice of the tree from the
// server and re-fetches a new slice whenever the user refocuses on someone else, so the amount drawn
// stays small however big the archive gets.
(function () {
  var scriptPromises = {};

  function loadScript(src) {
    if (!scriptPromises[src]) {
      scriptPromises[src] = new Promise(function (resolve, reject) {
        var el = document.createElement("script");
        el.src = src;
        el.onload = resolve;
        el.onerror = function () { delete scriptPromises[src]; reject(new Error("Failed to load " + src)); };
        document.head.appendChild(el);
      });
    }
    return scriptPromises[src];
  }

  function loadStyle(href) {
    if (document.querySelector('link[rel="stylesheet"][href="' + href + '"]')) return;
    var el = document.createElement("link");
    el.rel = "stylesheet";
    el.href = href;
    document.head.appendChild(el);
  }

  function escapeHtml(text) {
    return String(text == null ? "" : text).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }

  // Phones get a shallower default so the first view is legible without zooming.
  function isNarrow() {
    return window.matchMedia("(max-width: 40rem)").matches;
  }

  function init(container) {
    if (container.dataset.ready) return;
    container.dataset.ready = "1";

    var page = container.closest(".graph-page");
    var upSelect = page.querySelector("[data-graph-up]");
    var downSelect = page.querySelector("[data-graph-down]");
    var fitButton = page.querySelector("[data-graph-fit]");
    var status = page.querySelector("[data-graph-status]");
    var dataUrl = container.dataset.dataUrl;

    if (isNarrow()) {
      upSelect.value = Math.min(upSelect.value, 2);
      downSelect.value = Math.min(downSelect.value, 1);
    }

    function setStatus(text) {
      status.textContent = text;
      status.hidden = !text;
    }

    function fetchSlice(id) {
      var url = dataUrl.replace("__ID__", encodeURIComponent(id)) +
        "?up=" + encodeURIComponent(upSelect.value) + "&down=" + encodeURIComponent(downSelect.value);
      return fetch(url, { headers: { Accept: "application/json" } }).then(function (response) {
        if (!response.ok) throw new Error("Server returned " + response.status);
        return response.json();
      });
    }

    var chart = null;
    var focusId = container.dataset.personId;
    var requestSeq = 0;

    function cardHtml(d) {
      var person = d.data.data;
      var hidden = (person.hiddenParents || 0) + (person.hiddenChildren || 0);
      var more = hidden > 0
        ? '<span class="graph-more" title="' + hidden + ' more relatives not shown">+' + hidden + "</span>"
        : "";
      return (
        '<div class="card-inner card-rect graph-card graph-sex-' + escapeHtml(person.sex) + '">' +
        '<a class="graph-card-name" href="' + escapeHtml(person.url) + '">' + escapeHtml(person.name) + "</a>" +
        (person.maidenName ? '<div class="graph-card-maiden">(née ' + escapeHtml(person.maidenName) + ")</div>" : "") +
        (person.lifespan ? '<div class="graph-card-dates">' + escapeHtml(person.lifespan) + "</div>" : "") +
        more +
        "</div>"
      );
    }

    // Tapping a card re-centres the graph on that person (fetching their surroundings); the name itself is
    // a normal link to the profile, which htmx boosts like any other.
    function onCardClick(e, d) {
      if (e.target.closest("a")) return;
      refocus(d.data.id);
    }

    function refocus(id) {
      var seq = ++requestSeq;
      setStatus("Loading…");
      return fetchSlice(id).then(function (data) {
        if (seq !== requestSeq || !container.isConnected) return; // superseded, or navigated away
        focusId = id;
        chart.updateData(data);
        chart.updateMainId(id);
        chart.updateTree({ tree_position: "main_to_middle" });
        setStatus("");
      }).catch(function (err) {
        if (seq === requestSeq) setStatus("Couldn't load the graph (" + err.message + ").");
      });
    }

    loadStyle(container.dataset.styles);

    // d3 must be in place before family-chart, which reads it as a global when it loads.
    container.dataset.scripts.split("|").reduce(function (chain, src) {
      return chain.then(function () { return loadScript(src); });
    }, Promise.resolve())
      .then(function () { return fetchSlice(focusId); })
      .then(function (data) {
        if (!container.isConnected) return;

        chart = window.f3.createChart(container, data)
          .setTransitionTime(window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : 400)
          .setCardXSpacing(200)
          .setCardYSpacing(110)
          .setSingleParentEmptyCard(false)
          .setShowSiblingsOfMain(false)
          .setAncestryDepth(Number(upSelect.max) + 1)
          .setProgenyDepth(Number(downSelect.max) + 1);

        chart.setCardHtml()
          .setStyle("rect")
          .setCardDim({ w: 170, h: 70 })
          .setCardInnerHtmlCreator(cardHtml)
          .setOnCardClick(onCardClick)
          .setOnCardUpdate(function () { if (window.htmx) window.htmx.process(this); });

        chart.updateMainId(focusId);
        chart.updateTree({ initial: true });
        setStatus("");
      })
      .catch(function (err) {
        setStatus("Couldn't load the graph (" + err.message + ").");
      });

    upSelect.addEventListener("change", function () { refocus(focusId); });
    downSelect.addEventListener("change", function () { refocus(focusId); });
    fitButton.addEventListener("click", function () {
      if (chart) chart.updateTree({ tree_position: "fit" });
    });

    // Keep the graph fitted when the viewport changes (rotating a phone, resizing a window).
    if ("ResizeObserver" in window) {
      var timer;
      new ResizeObserver(function () {
        clearTimeout(timer);
        timer = setTimeout(function () {
          if (chart && container.isConnected) chart.updateTree({ tree_position: "fit", transition_time: 0 });
        }, 200);
      }).observe(container);
    }
  }

  function initAll(root) {
    (root || document).querySelectorAll("[data-family-graph]").forEach(init);
  }

  // htmx:load fires for content swapped in by hx-boost navigation as well as the initial page.
  document.addEventListener("htmx:load", function (e) { initAll(e.target); });
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () { initAll(); });
  } else {
    initAll();
  }
})();
