# Completion template contract

The default completion interface is an **in-control overlay**, not an Avalonia `Popup` or a native window. It shares the editor's rendered visual tree and input routing. The template can style or replace its contents without replacing the document engine or viewport.

For compatibility with the template part naming, the overlay container is named `PART_CompletionPopup`, but its type is a regular `Control` (a `Border` in the default theme). Its list is named `PART_CompletionList` and has type `ListBox`. Place this overlay after `PART_ViewportHost` in the same grid cell, with left/top alignment and `IsVisible="False"`. The editor sets its width, margin and visibility and measures it before choosing a position below or above the caret. It stays inside the editor's viewport rather than opening a separate operating-system popup window.

Pointer focus entering the list is kept within the completion interaction. Double-click inserts the selected completion. Up/Down, Enter, Tab and Escape are routed while the list itself has focus; Escape returns focus to the original viewport. Requests remain revision-checked and results from providers that ignore cancellation are rejected if the document or caret changed.

The browser pointer tests require the list to have actual rendered bounds in the visual tree; an open-state flag alone does not satisfy validation. The minimal theme deliberately omits these optional parts, so it does not show completion UI.
