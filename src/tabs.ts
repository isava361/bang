import { state } from "./state.ts";
import type { TabName } from "./state.ts";

const MOBILE_BREAKPOINT = 768;

export const initTabs = (): void => {
  const tabButtons = document.querySelectorAll<HTMLButtonElement>("[data-tab]");

  tabButtons.forEach((btn) => {
    btn.addEventListener("click", () => {
      const tab = btn.dataset.tab as TabName;
      if (!tab) return;
      state.activeTab = tab;

      // Update active button
      tabButtons.forEach((b) => b.classList.toggle("active", b.dataset.tab === tab));

      // Show/hide sections
      const tabSections = document.querySelectorAll<HTMLElement>("[data-tab-section]");
      tabSections.forEach((section) => {
        section.classList.toggle("hidden", section.dataset.tabSection !== tab);
      });

      // Info tab = show actions in game-header
      const gameHeader = document.querySelector(".game-header");
      gameHeader?.classList.toggle("show-actions", tab === "info");
    });
  });

  window.addEventListener("resize", () => updateMobileState());
  updateMobileState();
};

export const updateMobileState = (): void => {
  state.isMobile = window.innerWidth < MOBILE_BREAKPOINT;
  const gamePanel = document.getElementById("gamePanel");
  const tabBar = document.getElementById("tabBar");
  const tabSections = document.querySelectorAll<HTMLElement>("[data-tab-section]");
  const gameHeader = document.querySelector(".game-header");
  const inGame = gamePanel != null && !gamePanel.classList.contains("hidden");

  // Tab bar: only show on mobile when in game
  tabBar?.classList.toggle("hidden", !state.isMobile || !inGame);

  // Body class for bottom padding
  document.body.classList.toggle("has-tab-bar", state.isMobile && inGame);

  if (state.isMobile && inGame) {
    // Show only active tab section
    tabSections.forEach((section) => {
      section.classList.toggle("hidden", section.dataset.tabSection !== state.activeTab);
    });
    gameHeader?.classList.toggle("show-actions", state.activeTab === "info");
  } else {
    // Desktop: show all sections, hide actions toggle
    tabSections.forEach((section) => section.classList.remove("hidden"));
    gameHeader?.classList.remove("show-actions");
  }
};
