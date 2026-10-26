// Small browser helpers called from Blazor via IJSRuntime.
window.intakeLog = {
  load: key => localStorage.getItem(key),

  save: (key, value) => localStorage.setItem(key, value),

  // Ask the browser to keep this site's storage instead of evicting it under pressure.
  persist: async () =>
    navigator.storage && navigator.storage.persist ? await navigator.storage.persist() : false,

  download: (fileName, text) => {
    const blob = new Blob([text], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
};
