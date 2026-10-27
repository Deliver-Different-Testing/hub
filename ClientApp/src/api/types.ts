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
