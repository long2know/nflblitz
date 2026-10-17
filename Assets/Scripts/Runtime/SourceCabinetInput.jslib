mergeInto(LibraryManager.library, {
  SourceCabinetViewport: function (metric) {
    var canvas = Module["canvas"];
    var rect = canvas.getBoundingClientRect();
    switch (metric) {
      case 0: return rect.width;
      case 1: return rect.height;
      case 2: return canvas.width;
      case 3: return canvas.height;
      case 4: return window.devicePixelRatio || 1;
      default: return -1;
    }
  },
  SourceCabinetGamepad: function (station, metric) {
    var pads = navigator.getGamepads ? navigator.getGamepads() : [];
    var pad = pads[station];
    if (!pad || !pad.connected) return 0;
    if (pad.mapping !== "standard") return metric === 0 ? -1 : 0;
    switch (metric) {
      case 0: return 1;
      case 1: return pad.axes.length > 0 ? pad.axes[0] : 0;
      case 2: return pad.axes.length > 1 ? pad.axes[1] : 0;
      case 3:
        var held = 0;
        if (pad.buttons[0] && pad.buttons[0].pressed) held |= 1;
        if (pad.buttons[1] && pad.buttons[1].pressed) held |= 2;
        if (pad.buttons[5] && pad.buttons[5].pressed) held |= 4;
        return held;
      default: return -1;
    }
  }
});
