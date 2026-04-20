export function scrollApprovalIntoView(container, itemId) {
    if (!container || !itemId) {
        return "missing-container-or-id";
    }

    const selectedItem = container.querySelector(`#${CSS.escape(itemId)}`);
    if (!selectedItem) {
        return "item-not-found";
    }

    const containerRect = container.getBoundingClientRect();
    const itemRect = selectedItem.getBoundingClientRect();
    const isAbove = itemRect.top < containerRect.top;
    const isBelow = itemRect.bottom > containerRect.bottom;

    if (isAbove || isBelow) {
        selectedItem.scrollIntoView({
            block: "center",
            inline: "nearest",
            behavior: "auto"
        });
        return "scrolled";
    }

    return "already-visible";
}
