import { h } from "vue";
import DefaultTheme from "vitepress/theme";
import AsideDownload from "./AsideDownload.vue";
import "./custom.css";

export default {
    extends: DefaultTheme,
    Layout() {
        return h(DefaultTheme.Layout, null, {
            "aside-ads-before": () => h(AsideDownload),
        });
    },
};
