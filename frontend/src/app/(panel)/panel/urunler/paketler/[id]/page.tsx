import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";

import { PackageEditRoute } from "@/components/catalog/package-route";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Paket" };

export default function PackagePage() {
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link href="/panel/urunler?sekme=paketler" className="w-fit text-sm font-semibold text-neutral-700 hover:text-black">
          ← Paketler
        </Link>
        <h1 className="text-2xl font-bold tracking-tight text-black">Paket</h1>
      </div>
      <Suspense>
        <PackageEditRoute />
      </Suspense>
    </div>
  );
}
