// Horizontal scroll of the Schedule grid. The grid loads a window of days; .NET moves the window.
const edge = 40; // px from the side where a drag scrolls the grid
const speed = 600; // px per second: by time, so slow frames (the grid redraws) do not slow the scroll

const dayWidth = el => el.querySelector('.header .head')?.getBoundingClientRect().width || 1;

export function scrollToDay(el, index) {
    el.scrollLeft = index * dayWidth(el);
}

export function init(el, dotnet) {
    // Report the first visible day when the scroll stops.
    let timer = 0;
    el.addEventListener('scroll', () => {
        clearTimeout(timer);
        timer = setTimeout(() => {
            const width = dayWidth(el);
            const index = Math.round(el.scrollLeft / width);
            // Snap to whole days: a part-visible day would clip the bar controls.
            if (el.scrollLeft !== index * width) el.scrollLeft = index * width;
            dotnet.invokeMethodAsync('Scrolled', index);
        }, 150);
    });

    // While a press that started in the grid is held near or past a side, scroll that way.
    // A pick (first day clicked, the preview bar shows) does the same while the pointer is over the grid: no button is held then.
    // The pointer may not move, so tell the day cell under it that the pointer entered.
    let pressed = false, pointer = null, frame = 0, last = 0, entered = null;
    const step = now => {
        frame = 0;
        // A pick ends with its second click (or Escape): then the edge no longer scrolls.
        if (pointer?.pick && !el.querySelector('.bar.preview')) pointer = null;
        if (!pointer) { last = 0; return; }
        const dt = last ? Math.min(now - last, 250) : 16;
        last = now;
        const box = el.getBoundingClientRect();
        const label = el.querySelector('.header .label')?.getBoundingClientRect().right ?? box.left;
        const px = Math.max(1, Math.round(speed * dt / 1000));
        const dx = pointer.x > box.right - edge ? px : pointer.x < label + edge ? -px : 0;
        // A pick scrolls only from the edge zones over the days, not from the names or outside the grid.
        if (!dx || (pointer.pick && (pointer.x < label || pointer.x > box.right))) { last = 0; return; }
        const before = el.scrollLeft;
        el.scrollLeft += dx;
        // At the end of the loaded days, keep watching: .NET loads the next days, then the scroll goes on.
        if (el.scrollLeft === before) { frame = requestAnimationFrame(step); return; }
        const x = Math.min(Math.max(pointer.x, label + 1), box.right - 1);
        // Only a new day under the pointer is news for .NET: each event redraws the grid.
        // A pick skips it: the preview catches up when the mouse moves, and the second click sets the end day anyway.
        const cell = pointer.pick ? null : document.elementFromPoint(x, pointer.y);
        if (cell && cell !== entered) {
            entered = cell;
            cell.dispatchEvent(new PointerEvent('pointerenter'));
            cell.dispatchEvent(new MouseEvent('mouseenter')); // the pick preview follows mouseenter
        }
        frame = requestAnimationFrame(step);
    };
    el.addEventListener('pointerdown', () => { pressed = true; });
    const move = e => {
        const box = el.getBoundingClientRect();
        const picking = el.querySelector('.bar.preview') && e.clientY >= box.top && e.clientY <= box.bottom;
        pointer = pressed && e.buttons ? { x: e.clientX, y: e.clientY } : picking ? { x: e.clientX, y: e.clientY, pick: true } : null;
        if (pointer && !frame) frame = requestAnimationFrame(step);
    };
    const stop = () => {
        if (pressed) dotnet.invokeMethodAsync('Released');
        pressed = false; pointer = null;
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', stop);
    return {
        dispose: () => {
            clearTimeout(timer); cancelAnimationFrame(frame);
            window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', stop);
        },
    };
}
