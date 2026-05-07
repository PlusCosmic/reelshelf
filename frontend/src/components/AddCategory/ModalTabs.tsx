export type AddCategoryTab = "search" | "custom";

export function ModalTabs({
  activeTab,
  setActiveTab,
}: {
  activeTab: AddCategoryTab;
  setActiveTab: (tab: AddCategoryTab) => void;
}) {
  return (
    <div className="rs-modal-tabs" role="tablist" aria-label="Category type">
      <button
        className={`rs-modal-tab${activeTab === "search" ? " active" : ""}`}
        type="button"
        role="tab"
        aria-selected={activeTab === "search"}
        onClick={() => setActiveTab("search")}
      >
        Search games
      </button>
      <button
        className={`rs-modal-tab${activeTab === "custom" ? " active" : ""}`}
        type="button"
        role="tab"
        aria-selected={activeTab === "custom"}
        onClick={() => setActiveTab("custom")}
      >
        Custom category
      </button>
    </div>
  );
}
