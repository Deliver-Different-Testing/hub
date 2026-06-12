type SortDirection = 'ascending' | 'descending';

const HIDDEN_CLASS = 'fuel-row-hidden';

function cellSortValue(row: HTMLTableRowElement, index: number): string {
    const cell = row.children[index] as HTMLElement | undefined;
    return (cell?.dataset.sortValue ?? cell?.textContent ?? '').trim();
}

function compareRows(columnIndex: number, direction: SortDirection) {
    const sign = direction === 'descending' ? -1 : 1;
    return (a: HTMLTableRowElement, b: HTMLTableRowElement): number => {
        const av = cellSortValue(a, columnIndex);
        const bv = cellSortValue(b, columnIndex);
        const an = Number(av);
        const bn = Number(bv);
        const bothNumeric = av !== '' && bv !== '' && !Number.isNaN(an) && !Number.isNaN(bn);
        return bothNumeric
            ? sign * (an - bn)
            : sign * av.localeCompare(bv, undefined, {numeric: true, sensitivity: 'base'});
    };
}

interface Paginator {
    reset(): void;
}

function createPaginator(rows: HTMLTableRowElement[], pageSize: number): Paginator {
    const totalRows = rows.length;
    const totalPages = Math.max(1, Math.ceil(totalRows / pageSize));

    const pagination = document.querySelector<HTMLElement>('[data-fuel-pagination]');
    const status = pagination?.querySelector<HTMLElement>('[data-fuel-status]') ?? null;
    const range = pagination?.querySelector<HTMLElement>('[data-fuel-range]') ?? null;
    const prevBtn = pagination?.querySelector<HTMLButtonElement>('[data-fuel-prev]') ?? null;
    const nextBtn = pagination?.querySelector<HTMLButtonElement>('[data-fuel-next]') ?? null;
    let page = 1;

    function render(): void {
        const start = (page - 1) * pageSize;
        const end = Math.min(start + pageSize, totalRows);
        rows.forEach((row, i) => row.classList.toggle(HIDDEN_CLASS, i < start || i >= end));
        if (status) status.textContent = `Page ${page} of ${totalPages}`;
        if (range) {
            range.textContent = totalRows === 0
                ? 'No records'
                : `Showing ${start + 1}–${end} of ${totalRows}`;
        }
        if (prevBtn) prevBtn.disabled = page === 1;
        if (nextBtn) nextBtn.disabled = page === totalPages;
    }

    prevBtn?.addEventListener('click', () => {
        if (page > 1) {
            page--;
            render();
        }
    });
    nextBtn?.addEventListener('click', () => {
        if (page < totalPages) {
            page++;
            render();
        }
    });

    return {
        reset(): void {
            page = 1;
            render();
        },
    };
}

function wireSortableHeaders(
    table: HTMLTableElement,
    tbody: HTMLElement,
    rows: HTMLTableRowElement[],
    onSort: () => void,
): void {
    const headers = Array.from(table.querySelectorAll<HTMLTableCellElement>('thead th[data-fuel-sort]'));
    headers.forEach((th, index) => {
        th.setAttribute('tabindex', '0');
        th.setAttribute('role', 'button');
        th.addEventListener('click', () => {
            const next: SortDirection = th.getAttribute('aria-sort') === 'ascending' ? 'descending' : 'ascending';
            headers.forEach(other => other.setAttribute('aria-sort', 'none'));
            th.setAttribute('aria-sort', next);
            rows.sort(compareRows(index, next));
            const frag = document.createDocumentFragment();
            rows.forEach(r => frag.appendChild(r));
            tbody.appendChild(frag);
            onSort();
        });
        th.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                th.click();
            }
        });
    });
}

document.addEventListener('DOMContentLoaded', () => {
    const table = document.querySelector<HTMLTableElement>('.fuel-history-table');
    const tbody = table?.querySelector('tbody');
    if (!table || !tbody) return;

    const rows = Array.from(tbody.querySelectorAll<HTMLTableRowElement>('tr'));
    const pageSize = Number.parseInt(table.dataset.fuelPageSize ?? '', 10) || rows.length || 1;

    const paginator = createPaginator(rows, pageSize);
    wireSortableHeaders(table, tbody, rows, () => paginator.reset());
});
