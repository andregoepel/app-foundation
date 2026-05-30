window.site = (() => {
    let _heroStop = null;
    let _errStop = null;
    let _rafCursor = 0;

    function getInitialState() {
        const lang = (() => {
            const s = localStorage.getItem('ag-lang');
            if (s === 'en' || s === 'de') return s;
            return (navigator.language || '').startsWith('de') ? 'de' : 'en';
        })();
        const theme = (() => {
            const s = localStorage.getItem('ag-theme');
            return (s === 'light' || s === 'dark' || s === 'system') ? s : 'dark';
        })();
        const resolvedTheme = theme === 'system'
            ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
            : theme;
        return { lang, theme, resolvedTheme };
    }

    function savePrefs(lang, theme) {
        localStorage.setItem('ag-lang', lang);
        localStorage.setItem('ag-theme', theme);
    }

    function applyAttrs(resolvedTheme, lang) {
        const html = document.documentElement;
        html.dataset.theme = resolvedTheme;
        html.dataset.userTheme = resolvedTheme;
        html.lang = lang;
    }

    // ── Shared constellation canvas ──────────────────────────────────────────
    // Returns a stop() function. Caller is responsible for calling stop()
    // before re-initialising or navigating away.
    function initCanvasBg(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return () => {};

        const ctx = canvas.getContext('2d');
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        let w = 0, h = 0;

        const resize = () => {
            const r = canvas.getBoundingClientRect();
            w = r.width; h = r.height;
            canvas.width = Math.round(w * dpr);
            canvas.height = Math.round(h * dpr);
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        };
        resize();
        const ro = new ResizeObserver(resize);
        ro.observe(canvas);

        const nodes = Array.from({ length: 36 }, () => ({
            x: Math.random(), y: Math.random(),
            vx: (Math.random() - 0.5) * 0.00018,
            vy: (Math.random() - 0.5) * 0.00018,
            r: 0.8 + Math.random() * 1.4,
        }));

        let mouse = { x: 0.5, y: 0.5, active: false };
        const onMove = e => {
            const r = canvas.getBoundingClientRect();
            mouse.x = (e.clientX - r.left) / r.width;
            mouse.y = (e.clientY - r.top) / r.height;
            mouse.active = true;
        };
        const onLeave = () => { mouse.active = false; };
        window.addEventListener('mousemove', onMove);
        window.addEventListener('mouseleave', onLeave);

        const t0 = performance.now();
        let raf = 0;

        const draw = () => {
            const t = (performance.now() - t0) / 1000;
            const accent = getComputedStyle(document.documentElement).getPropertyValue('--accent').trim();
            ctx.clearRect(0, 0, w, h);

            const gridColor = getComputedStyle(canvas).getPropertyValue('--grid-line').trim() || 'rgba(160,190,230,0.05)';
            ctx.lineWidth = 1;
            ctx.strokeStyle = gridColor;
            ctx.beginPath();
            for (let x = 0; x <= w; x += 56) { ctx.moveTo(x + 0.5, 0); ctx.lineTo(x + 0.5, h); }
            for (let y = 0; y <= h; y += 56) { ctx.moveTo(0, y + 0.5); ctx.lineTo(w, y + 0.5); }
            ctx.stroke();

            const scanY = (Math.sin(t * 0.18) * 0.5 + 0.5) * h;
            const grad = ctx.createLinearGradient(0, scanY - 80, 0, scanY + 80);
            grad.addColorStop(0, 'transparent');
            grad.addColorStop(0.5, accent + '22');
            grad.addColorStop(1, 'transparent');
            ctx.fillStyle = grad;
            ctx.fillRect(0, scanY - 80, w, 160);

            for (const n of nodes) {
                n.x += n.vx; n.y += n.vy;
                if (n.x < 0 || n.x > 1) n.vx *= -1;
                if (n.y < 0 || n.y > 1) n.vy *= -1;
            }

            const mx = mouse.active ? mouse.x : 0.5;
            const my = mouse.active ? mouse.y : 0.5;
            const px = nodes.map(n => ({
                x: n.x * w + (mx - 0.5) * 18,
                y: n.y * h + (my - 0.5) * 18,
                r: n.r,
            }));

            ctx.lineWidth = 1;
            for (let i = 0; i < px.length; i++) {
                for (let j = i + 1; j < px.length; j++) {
                    const dx = px[i].x - px[j].x, dy = px[i].y - px[j].y;
                    const d = Math.hypot(dx, dy);
                    if (d < 160) {
                        const a = (1 - d / 160) * 0.35;
                        ctx.strokeStyle = accent + Math.round(a * 255).toString(16).padStart(2, '0');
                        ctx.beginPath();
                        ctx.moveTo(px[i].x, px[i].y);
                        ctx.lineTo(px[j].x, px[j].y);
                        ctx.stroke();
                    }
                }
            }

            ctx.fillStyle = accent;
            for (const p of px) {
                ctx.beginPath();
                ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
                ctx.fill();
            }

            raf = requestAnimationFrame(draw);
        };
        raf = requestAnimationFrame(draw);

        return () => {
            cancelAnimationFrame(raf);
            ro.disconnect();
            window.removeEventListener('mousemove', onMove);
            window.removeEventListener('mouseleave', onLeave);
        };
    }

    function initHeroBg() {
        if (_heroStop) { _heroStop(); _heroStop = null; }
        _heroStop = initCanvasBg('hero-canvas');
    }

    function initErrorBg() {
        if (_errStop) { _errStop(); _errStop = null; }
        _errStop = initCanvasBg('err-canvas');
    }

    function stopErrorBg() {
        if (_errStop) { _errStop(); _errStop = null; }
    }

    function initCursor() {
        const dot = document.createElement('div');
        dot.className = 'cursor-dot';
        const ring = document.createElement('div');
        ring.className = 'cursor-ring';
        document.body.appendChild(dot);
        document.body.appendChild(ring);

        let mx = window.innerWidth / 2, my = window.innerHeight / 2;
        let rx = mx, ry = my;

        const onMove = e => {
            mx = e.clientX; my = e.clientY;
            dot.style.transform = `translate(${mx}px, ${my}px) translate(-50%, -50%)`;
        };
        const onOver = e => {
            const interactive = e.target && e.target.closest('a, button, .svc-card, [role="button"]');
            ring.classList.toggle('is-hover', !!interactive);
        };
        const tick = () => {
            rx += (mx - rx) * 0.18;
            ry += (my - ry) * 0.18;
            ring.style.transform = `translate(${rx}px, ${ry}px) translate(-50%, -50%)`;
            _rafCursor = requestAnimationFrame(tick);
        };

        window.addEventListener('mousemove', onMove);
        window.addEventListener('mouseover', onOver);
        _rafCursor = requestAnimationFrame(tick);
    }

    function initScrollSpy() {
        const ids = ['problem', 'services', 'about', 'cases', 'contact'];
        const update = () => {
            const y = window.scrollY + window.innerHeight * 0.35;
            let current = 'top';
            for (const id of ids) {
                const el = document.getElementById(id);
                if (el && el.offsetTop <= y) current = id;
            }
            document.querySelectorAll('[data-nav-section]').forEach(el => {
                el.classList.toggle('is-active', el.dataset.navSection === current);
            });
        };
        update();
        window.addEventListener('scroll', update, { passive: true });
    }

    function initHeaderScroll() {
        const update = () => {
            const hdr = document.querySelector('.hdr');
            if (hdr) hdr.classList.toggle('is-scrolled', window.scrollY > 8);
        };
        update();
        window.addEventListener('scroll', update, { passive: true });
    }

    function initReveal() {
        const io = new IntersectionObserver(
            entries => entries.forEach(e => {
                if (e.isIntersecting) { e.target.classList.add('is-shown'); io.unobserve(e.target); }
            }),
            { threshold: 0.12, rootMargin: '0px 0px -8% 0px' }
        );
        document.querySelectorAll('.reveal:not(.is-shown)').forEach(el => io.observe(el));
    }

    function refreshReveal() {
        setTimeout(() => {
            const io = new IntersectionObserver(
                entries => entries.forEach(e => {
                    if (e.isIntersecting) { e.target.classList.add('is-shown'); io.unobserve(e.target); }
                }),
                { threshold: 0.12, rootMargin: '0px 0px -8% 0px' }
            );
            document.querySelectorAll('.reveal').forEach(el => {
                const rect = el.getBoundingClientRect();
                if (rect.bottom < window.innerHeight * 1.1) {
                    el.classList.add('is-shown');
                } else {
                    el.classList.remove('is-shown');
                    io.observe(el);
                }
            });
        }, 50);
    }

    function initCardGlow() {
        window.addEventListener('mousemove', e => {
            document.querySelectorAll('.svc-card').forEach(card => {
                const r = card.getBoundingClientRect();
                card.style.setProperty('--mx', ((e.clientX - r.left) / r.width * 100) + '%');
                card.style.setProperty('--my', ((e.clientY - r.top) / r.height * 100) + '%');
            });
        }, { passive: true });
    }

    function initSystemThemeListener() {
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', e => {
            const html = document.documentElement;
            if (html.dataset.userTheme === 'system') {
                html.dataset.theme = e.matches ? 'dark' : 'light';
            }
        });
    }

    function initAll() {
        initScrollSpy();
        initHeaderScroll();
        initReveal();
        initCardGlow();
        initCursor();
        initHeroBg();
        initSystemThemeListener();
    }

    return { getInitialState, savePrefs, applyAttrs, initAll, refreshReveal, initErrorBg, stopErrorBg };
})();
