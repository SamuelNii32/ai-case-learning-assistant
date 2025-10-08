// src/components/Header.jsx
import { Link } from "react-router-dom";
import { Button } from "./ui/button";
import { FileText } from "lucide-react";

export default function Header() {
  return (
    <header className="border-b border-slate-200">
      <div className="container mx-auto px-6 h-16 flex items-center justify-between">
        <Link to="/" className="flex items-center gap-2">
          <div className="w-10 h-10 bg-blue-600 rounded-lg flex items-center justify-center">
            <FileText className="w-6 h-6 text-white" />
          </div>
          <span className="font-semibold text-xl text-slate-900">AI Case Assistant</span>
        </Link>
        <nav className="flex items-center gap-8">
          <Link to="/about" className="text-slate-600 hover:text-slate-900 transition-colors">
            About
          </Link>
          <Link to="/privacy" className="text-slate-600 hover:text-slate-900 transition-colors">
            Privacy
          </Link>
          <Link to="/contact" className="text-slate-600 hover:text-slate-900 transition-colors">
            Contact
          </Link>
          <Link to="/login">
            <Button variant="outline">Sign In</Button>
          </Link>
        </nav>
      </div>
    </header>
  );
}
