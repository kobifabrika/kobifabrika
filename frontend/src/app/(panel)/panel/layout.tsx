import { BottomNav } from "@/components/panel/bottom-nav";
import { Sidebar } from "@/components/panel/sidebar";
import { Topbar } from "@/components/panel/topbar";
import { QueryProvider } from "@/components/providers/query-provider";

/**
 * İşletme paneli düzeni: masaüstünde (lg+) koyu sol menü, telefonda alt sekme çubuğu.
 * Canlıda app.kobifabrika.com bu yollara yönlenecek (karar günlüğü, 2026-10-10).
 */
export default function PanelLayout({ children }: LayoutProps<"/panel">) {
  return (
    <QueryProvider>
      <div className="min-h-screen bg-background">
        <Sidebar />
        <div className="flex min-h-screen flex-col lg:pl-64">
          <Topbar />
          <main className="flex-1 px-4 pt-5 pb-28 lg:px-8 lg:pt-8 lg:pb-10">{children}</main>
        </div>
        <BottomNav />
      </div>
    </QueryProvider>
  );
}
