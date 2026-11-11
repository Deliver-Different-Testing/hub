export interface Tenant {
  id: number;
  name: string;
  code: string;
  countryCode: string | null;
}

export interface Me {
  email: string;
  displayName: string | null;
  isCourier: boolean;
  currentTenant: Tenant | null;
  tenants: Tenant[];
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
  isCourierLogin: boolean;
  returnUrl?: string;
}

export interface RedirectResponse {
  redirectUrl: string | null;
}

export interface TenantSetting {
  id: number;
  name: string;
  value: string | null;
}

export interface HubTile {
  key: string;
  title: string;
  icon: string;
  href: string;
  isDisabled: boolean;
}

export interface HomeData {
  userEmail: string;
  tenantCode: string;
  today: string;
  isCourier: boolean;
  showAfterHours: boolean;
  tiles: HubTile[];
}

export interface FuelSurchargeRow {
  fuelSurchargeId: number;
  clientId: number | null;
  clientName: string | null;
  rate: number | null;
  pumpPrice: number | null;
  start: string | null;
  end: string | null;
  active: boolean;
  isCurrent: boolean;
  scopeLabel: string;
}

export interface FuelSurchargeData {
  hasData: boolean;
  today: string;
  pumpPrice: number | null;
  currentStandard: FuelSurchargeRow | null;
  history: FuelSurchargeRow[];
}

export interface LogoResponse {
  success: boolean;
  logoUrl: string | null;
  isLocal?: boolean;
}

export interface KbSource {
  articleId: string;
  title: string;
  isFaq: boolean;
}

export interface AiChatRequest {
  messages: { role: string; content: string }[];
  pageContext?: string | null;
}

export interface StreamEvent {
  type: 'status' | 'text_delta' | 'tool_use' | 'done' | 'error';
  text?: string | null;
  toolName?: string | null;
  error?: string | null;
  sources?: KbSource[] | null;
}

/** One hidden field of the original authorization request, posted back to /connect/authorize. */
export interface AgentAccessParameter {
  name: string;
  value: string;
}

/** GET api/agent-access/connect: what the AI-app connect page shows, from the ticket the authorize endpoint sealed. */
export interface AgentAccessConnectPage {
  kind: 'consent' | 'switch';
  clientName: string;
  /** Only for apps DFRNT configured itself; a metadata document's name is the app's own claim. */
  verifiedVendor: string | null;
  /** The host of a URL client id (a client metadata document). */
  clientHost: string | null;
  resourceHost: string;
  redirectHost: string | null;
  currentOrganisation: string | null;
  hubOrganisation: string | null;
  canSwitch: boolean;
  error: string | null;
  parameters: AgentAccessParameter[];
  antiforgeryFieldName: string;
  antiforgeryToken: string;
}
