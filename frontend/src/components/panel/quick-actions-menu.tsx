"use client";

import { Plus } from "lucide-react";
import Link from "next/link";

import { useSession } from "@/components/providers/session-provider";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { visibleQuickActions } from "@/lib/navigation";

/** Masaüstü üst çubukta (+) hızlı işlem menüsü; telefonda aynı liste alt çubuktaki (+) düğmesinde. */
export function QuickActionsMenu() {
  const { role, business } = useSession();
  const actions = visibleQuickActions(role, business.modules);
  if (actions.length === 0) return null;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label="Hızlı işlem"
        className="hidden h-9 items-center gap-2 rounded-lg bg-primary px-4 text-sm font-semibold text-primary-foreground lg:flex"
      >
        <Plus aria-hidden className="size-4" />
        Yeni
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        {actions.map(({ label, href, icon: Icon }) => (
          <DropdownMenuItem key={href} asChild>
            <Link href={href} className="min-h-11">
              <Icon aria-hidden />
              {label}
            </Link>
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
