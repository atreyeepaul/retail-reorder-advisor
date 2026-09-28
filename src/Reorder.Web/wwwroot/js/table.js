// Click-to-sort for the action-list table. Progressive enhancement — the page works without it.
(function () {
    const table = document.getElementById('recs');
    if (!table || !table.tHead || !table.tBodies.length) {
        return;
    }

    const tbody = table.tBodies[0];
    const headers = table.tHead.rows[0].cells;
    let sortedCol = -1;
    let ascending = true;

    function value(row, index, type) {
        const cell = row.cells[index];
        if (type === 'num') {
            const n = parseFloat(cell.getAttribute('data-value'));
            return isNaN(n) ? Infinity : n;   // blanks / "—" sort last
        }
        return (cell.textContent || '').trim().toLowerCase();
    }

    function sortBy(index, header) {
        const type = header.getAttribute('data-sort');
        ascending = sortedCol === index ? !ascending : true;
        sortedCol = index;

        Array.from(tbody.rows)
            .sort(function (a, b) {
                const av = value(a, index, type);
                const bv = value(b, index, type);
                const cmp = type === 'num' ? av - bv : String(av).localeCompare(String(bv));
                return ascending ? cmp : -cmp;
            })
            .forEach(function (row) { tbody.appendChild(row); });

        for (const h of header.parentNode.cells) {
            h.classList.remove('sort-asc', 'sort-desc');
        }
        header.classList.add(ascending ? 'sort-asc' : 'sort-desc');
    }

    for (let i = 0; i < headers.length; i++) {
        if (headers[i].hasAttribute('data-sort')) {
            headers[i].addEventListener('click', sortBy.bind(null, i, headers[i]));
        }
    }
})();
