import { LegalPage, legalMetadata } from "@/components/site/legal-page";
import { legalDocs } from "@/content/legal";

export const metadata = legalMetadata(legalDocs.cerez);

export default function CookiePage() {
  return <LegalPage doc={legalDocs.cerez} />;
}
