// Date entry: the text field takes DD-MON-YYYY (or partial/qualified dates); the calendar button opens the
// native date picker (which is what mobile browsers show) and writes the chosen date back as DD-MON-YYYY.
(function () {
  var months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

  function toDisplay(iso) {
    var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
    return m ? m[3] + "-" + months[parseInt(m[2], 10) - 1] + "-" + m[1] : "";
  }

  function toIso(text) {
    var m = /^(\d{1,2})-([A-Za-z]{3})-(\d{4})$/.exec((text || "").trim());
    if (!m) return "";
    var idx = months.findIndex(function (x) { return x.toLowerCase() === m[2].toLowerCase(); });
    if (idx < 0) return "";
    return m[3] + "-" + String(idx + 1).padStart(2, "0") + "-" + m[1].padStart(2, "0");
  }

  // Delegated, so it keeps working after htmx swaps content in.
  document.addEventListener("click", function (e) {
    var button = e.target.closest(".date-picker-button");
    if (!button) return;
    var wrapper = button.closest(".date-input");
    var picker = wrapper.querySelector(".date-picker");
    picker.value = toIso(wrapper.querySelector("input[type=text]").value);
    if (typeof picker.showPicker === "function") {
      picker.showPicker();
    } else {
      picker.focus();
      picker.click();
    }
  });

  document.addEventListener("change", function (e) {
    if (!e.target.classList || !e.target.classList.contains("date-picker")) return;
    var text = e.target.closest(".date-input").querySelector("input[type=text]");
    if (e.target.value) {
      text.value = toDisplay(e.target.value);
      text.dispatchEvent(new Event("input", { bubbles: true }));
    }
  });
})();
