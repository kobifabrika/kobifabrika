import { RequirePermission, SettingsNav } from "@/components/panel/settings-nav";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export default function SettingsLayout({ children }: LayoutProps<"/panel/ayarlar">) {
  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">Ayarlar</h1>
      <RequirePermission permission="viewSettings">
        <SettingsNav />
        {children}
      </RequirePermission>
    </div>
  );
}
