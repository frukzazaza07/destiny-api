import { permanentRedirect } from "next/navigation";

export default function RootRedirectPage() {
  permanentRedirect("/th/");
}
