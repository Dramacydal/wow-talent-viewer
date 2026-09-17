import { createApp, h } from 'vue';

/**
 * Minimal placeholder island: fetches the talent-tree JSON and lists tabs/talent counts, just
 * to prove the Symfony API <-> Vue wiring end-to-end. The actual tier/column grid with
 * click-to-build interactivity is a separate, later piece of work.
 *
 * Uses h() render functions, not a `template` string - importmap:require pulls Vue's
 * runtime-only build (vue.runtime.esm-bundler.js, no template compiler), so a string
 * template would throw at runtime.
 */
createApp({
    data() {
        return { tree: null, error: null };
    },
    async mounted() {
        const el = document.getElementById('talent-tree-app');
        try {
            const response = await fetch(el.dataset.treeUrl);
            if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
            this.tree = await response.json();
        } catch (e) {
            this.error = e.message;
        }
    },
    render() {
        if (this.error) return h('p', `Failed to load talent tree: ${this.error}`);
        if (!this.tree) return h('p', 'Loading...');

        return h('div', [
            h('h2', `${this.tree.class.name} - ${this.tree.build.label}`),
            h('ul', this.tree.tabs.map((tab) => h('li', { key: tab.id }, [
                tab.iconUrl ? h('img', { src: tab.iconUrl, width: 24, height: 24, alt: '' }) : null,
                ` ${tab.name} - ${tab.talents.length} talents`,
            ]))),
        ]);
    },
}).mount('#talent-tree-app');
