import { Suspense } from "react";

import { BottomNav } from "@/components/panel/bottom-nav";
import { EmailBanner } from "@/components/panel/email-banner";
import { PanelGate } from "@/components/panel/panel-gate";
import { Sidebar } from "@/components/panel/sidebar";
import { Topbar } from "@/components/panel/topbar";

/**
 * Panel içeriği istemcide oturum doğrulandıktan sonra render edilir (oturum API cookie'sinde; Next sunucusu
 * oturumu bilmez). Bu yüzden panel anında gezinme (instant navigation) doğrulamasının dışında.
 */
export const instant = false;

/**
 * İşletme paneli düzeni: masaüstünde (lg+) koyu sol menü, telefonda alt sekme çubuğu.
 * PanelGate oturumu ve kurulumu kontrol eder. Canlıda app.kobifabrika.com bu yollara yönlenecek.
 * Suspense: dinamik yollarda (/panel/musteriler/[id]) PanelGate'in usePathname'i build'de bilinmez; kabuk boş gelir.
 */
export default function PanelLayout({ children }: LayoutProps<"/panel">) {
  return (
    <Suspense>
      <PanelGate>
        <div className="min-h-screen bg-background">
          <Sidebar />
          <div className="flex min-h-screen flex-col lg:pl-64">
            <Topbar />
            <EmailBanner />
            <main className="flex-1 px-4 pt-5 pb-28 lg:px-8 lg:pt-8 lg:pb-10">{children}</main>
          </div>
          <BottomNav />
        </div>
      </PanelGate>
    </Suspense>
  );
}
