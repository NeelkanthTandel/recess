// Recess site: the hero's break sequence and the "Take a 20-second break" demo.
// No dependencies, no network requests, no storage.
(() => {
  "use strict";

  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const format = (s) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;

  // Hero: the screen dims once, counts down a short break, then the work comes back.
  const hero = document.querySelector("[data-hero-break]");
  const heroTime = document.querySelector("[data-hero-time]");
  const menubar = document.querySelector("[data-menubar-countdown]");

  if (hero && heroTime && menubar) {
    const length = 20;
    if (reduceMotion) {
      hero.classList.add("is-on", "is-static");
      menubar.textContent = "now";
    } else {
      let left = 2;
      menubar.textContent = `${left}s`;
      const lead = setInterval(() => {
        left -= 1;
        menubar.textContent = left > 0 ? `${left}s` : "now";
        if (left > 0) return;
        clearInterval(lead);
        hero.classList.add("is-on");
        let remaining = length;
        hero.style.setProperty("--progress", "1");
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

  // Full-page break.
  const overlay = document.querySelector("[data-break]");
  const opener = document.querySelector("[data-try-break]");
  if (!overlay || !opener) return;

  const time = overlay.querySelector("[data-break-time]");
  const ring = overlay.querySelector("[data-break-ring]");
  const title = overlay.querySelector("[data-break-title]");
  const tip = overlay.querySelector("[data-break-tip]");
  const status = overlay.querySelector("[data-break-status]");
  const buttons = [...overlay.querySelectorAll("[data-break-close]")];
  const length = 20;
  let timer = null;
  let returnFocus = null;

  const setRemaining = (s) => {
    time.textContent = format(s);
    ring.style.setProperty("--progress", String(s / length));
  };

  const close = () => {
    clearInterval(timer);
    timer = null;
    overlay.classList.remove("is-on");
    document.documentElement.classList.remove("is-resting");
    const finish = () => {
      overlay.hidden = true;
      buttons.forEach((b) => (b.hidden = false));
      if (returnFocus) returnFocus.focus();
    };
    if (reduceMotion) finish(); else setTimeout(finish, 600);
  };

  const open = () => {
    returnFocus = document.activeElement;
    title.textContent = "Look at something far away";
    tip.textContent = "Let your eyes relax on something at least six metres away. The page comes back by itself.";
    status.textContent = "";
    setRemaining(length);
    overlay.hidden = false;
    document.documentElement.classList.add("is-resting");
    requestAnimationFrame(() => overlay.classList.add("is-on"));
    buttons[0].focus();

    let remaining = length;
    timer = setInterval(() => {
      remaining -= 1;
      setRemaining(remaining);
      if (remaining > 0) return;
      clearInterval(timer);
      title.textContent = "That's a break";
      tip.textContent = "Twenty seconds, done. Recess keeps that rhythm going all day.";
      status.textContent = "Break finished.";
      buttons.forEach((b) => (b.hidden = true));
      setTimeout(() => close(), 2200);
    }, 1000);
  };

  opener.addEventListener("click", open);

  buttons.forEach((b) =>
    b.addEventListener("click", () => {
      // In the app these buttons really postpone or skip; here they just end the demo.
      close();
    })
  );

  overlay.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
      e.preventDefault();
      close();
      return;
    }
    if (e.key !== "Tab") return;
    // Keep keyboard focus inside the break while it's showing.
    const visible = buttons.filter((b) => !b.hidden);
    if (visible.length === 0) {
      e.preventDefault();
      return;
    }
    const first = visible[0];
    const last = visible[visible.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  });
})();
