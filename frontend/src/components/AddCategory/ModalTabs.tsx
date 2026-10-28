import { addGameCopy } from "@/copy/addGame";

export type AddCategoryTab = "search" | "custom";

export function ModalTabs({
  activeTab,
  setActiveTab,
}: {
  activeTab: AddCategoryTab;
  setActiveTab: (tab: AddCategoryTab) => void;
}) {
  return (
    <div
      className="rs-modal-tabs"
      role="tablist"
      aria-label={addGameCopy.tabs.label}
    >
      <button
        className={`rs-modal-tab${activeTab === "search" ? " active" : ""}`}
        type="button"
        role="tab"
        aria-selected={activeTab === "search"}
        onClick={() => setActiveTab("search")}
      >
        {addGameCopy.tabs.search}
      </button>
      <button
        className={`rs-modal-tab${activeTab === "custom" ? " active" : ""}`}
        type="button"
        role="tab"
        aria-selected={activeTab === "custom"}
        onClick={() => setActiveTab("custom")}
      >
        {addGameCopy.tabs.custom}
      </button>
    </div>
  );
}
