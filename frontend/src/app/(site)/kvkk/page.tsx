import { LegalPage, legalMetadata } from "@/components/site/legal-page";
import { legalDocs } from "@/content/legal";

export const metadata = legalMetadata(legalDocs.kvkk);

export default function KvkkPage() {
  return <LegalPage doc={legalDocs.kvkk} />;
}
