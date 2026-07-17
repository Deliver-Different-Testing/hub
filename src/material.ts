// Official Google Material Design 3 web components (@material/web).
//
// Import components INDIVIDUALLY — never barrel-import '@material/web' (that
// ships every component and destroys bundle size). Add a line here as each
// component is adopted, then rebuild.
//
// Theming: these components read MD3 system tokens (--md-sys-*). The bridge in
// wwwroot/css/material-theme.less maps those to the app's existing --dd-* brand
// tokens, so Material components inherit brand colour AND tenant theming for
// free (the --dd-* values cascade from body[data-tenant-country]).

// Phase 1 — interaction primitive used on the App Hub tiles.
import '@material/web/ripple/ripple.js';

// Button set — themed via the bridge now, used as surfaces migrate (Phase 1/2).
import '@material/web/button/filled-button.js';
import '@material/web/button/outlined-button.js';
import '@material/web/button/text-button.js';
import '@material/web/button/filled-tonal-button.js';

// Phase 2 — form fields.
import '@material/web/textfield/outlined-text-field.js';
import '@material/web/textfield/filled-text-field.js';
import '@material/web/select/filled-select.js';
import '@material/web/select/select-option.js';
import '@material/web/checkbox/checkbox.js';
import '@material/web/icon/icon.js';

// Communication — indeterminate progress bar shown while the login form submits.
import '@material/web/progress/linear-progress.js';

// Phase 3 — navigation menus (tenant selector, profile menu).
import '@material/web/menu/menu.js';
import '@material/web/menu/menu-item.js';
import '@material/web/divider/divider.js';

// Theme toggle — icon button trigger for the Light / Dark / System menu.
import '@material/web/iconbutton/icon-button.js';
