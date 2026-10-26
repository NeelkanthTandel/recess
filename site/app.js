// Recess site: the hero screen's break, and the 20-second break in the night section.
// No dependencies, no network requests, no storage.
(() => {
  "use strict";

  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const format = (s) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;

  // Downloads: offer the visitor's own system first. Without this script the page offers the
  // Mac download with a link for Windows, so nothing depends on it.
  const releases = "https://github.com/NeelkanthTandel/recess/releases/latest/download/";
  const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || navigator.userAgent || "";
  const onWindows = /win/i.test(platform) && !/darwin|mac/i.test(platform);
  const download = document.querySelector("[data-download]");
  const note = document.querySelector("[data-download-note]");
  if (onWindows && download && note) {
    download.href = releases + "Recess-Setup.exe";
    download.textContent = "Download for Windows";
    note.textContent = "For Windows 10 and 11, or ";
    const mac = document.createElement("a");
    mac.href = releases + "Recess.dmg";
    mac.textContent = "download for Mac";
    note.append(mac, ".");
  }

  // Install steps: Mac and Windows tabs, starting on the visitor's system.
  const install = document.querySelector(".install[data-os]");
  const tabs = document.querySelectorAll("[data-os-tab]");
  const showOs = (os) => {
    if (!install) return;
    install.dataset.os = os;
    tabs.forEach((tab) => tab.setAttribute("aria-selected", String(tab.dataset.osTab === os)));
  };
  tabs.forEach((tab) => {
    tab.addEventListener("click", () => showOs(tab.dataset.osTab));
    tab.addEventListener("keydown", (e) => {
      if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
      const next = tab.dataset.osTab === "mac" ? "windows" : "mac";
      showOs(next);
      document.querySelector(`[data-os-tab="${next}"]`).focus();
    });
  });
  if (onWindows) showOs("windows");

  // Hero: the floating screen dims once into a short break, then the work comes back.
  const hero = document.querySelector("[data-hero-break]");
  const heroTime = document.querySelector("[data-hero-time]");
  const menubar = document.querySelector("[data-menubar]");
  if (hero && heroTime && menubar) {
    const length = 20;
    if (reduceMotion) {
      hero.classList.add("is-on", "is-static");
      menubar.textContent = "now";
    } else {
      let lead = 3;
      menubar.textContent = `${lead}s`;
      const countIn = setInterval(() => {
        lead -= 1;
        menubar.textContent = lead > 0 ? `${lead}s` : "now";
        if (lead > 0) return;
        clearInterval(countIn);
        hero.classList.add("is-on");
        let remaining = length;
        const tick = setInterval(() => {
          remaining -= 1;
          heroTime.textContent = format(remaining);
          hero.style.setProperty("--progress", String(remaining / length));
          if (remaining > 0) return;
          clearInterval(tick);
          hero.classList.remove("is-on");
          menubar.textContent = "60m";
        }, 1000);
      }, 1000);
    }
  }

  // Night: a real 20-second break, on the giant arc.
  const night = document.querySelector("[data-night]");
  if (!night) return;
  const arc = night.querySelector(".arc-svg");
  const time = night.querySelector("[data-arc-time]");
  const title = night.querySelector("[data-night-title]");
  const text = night.querySelector("[data-night-text]");
  const start = night.querySelector("[data-arc-start]");
  const stop = night.querySelector("[data-arc-stop]");
  const status = night.querySelector("[data-arc-status]");
  const length = 20;
  const idle = { title: title.textContent, text: text.textContent };
  let timer = null;

  const show = (s) => {
    time.textContent = format(s);
    arc.style.setProperty("--progress", String(s / length));
  };

  const reset = (message) => {
    clearInterval(timer);
    timer = null;
    night.classList.remove("is-resting");
    stop.hidden = true;
    start.hidden = false;
    start.textContent = message ? "Take another" : "Start the break";
    if (message) {
      title.textContent = message.title;
      text.textContent = message.text;
      status.textContent = message.title;
    } else {
      title.textContent = idle.title;
      text.textContent = idle.text;
      show(length);
    }
  };

  const begin = () => {
    clearInterval(timer);
    let remaining = length;
    show(remaining);
    night.classList.add("is-resting");
    title.textContent = "Look at something far away.";
    text.textContent = "Let your eyes settle on the furthest thing you can see. The page will wait.";
    status.textContent = "Break started, 20 seconds.";
    start.hidden = true;
    stop.hidden = false;
    stop.focus({ preventScroll: true });
    timer = setInterval(() => {
      remaining -= 1;
      show(remaining);
      if (remaining > 0) return;
      reset({ title: "That's a break.", text: "Twenty seconds, done. Recess keeps that rhythm going all day." });
    }, 1000);
  };

  start.addEventListener("click", begin);
  stop.addEventListener("click", () => reset());
  night.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && timer) reset();
  });

  // "Take a 20-second break" in the hero: go to the night and start.
  document.querySelectorAll("[data-start-break]").forEach((link) =>
    link.addEventListener("click", (e) => {
      e.preventDefault();
      night.scrollIntoView({ behavior: reduceMotion ? "auto" : "smooth", block: "start" });
      setTimeout(begin, reduceMotion ? 0 : 650);
    })
  );
})();
