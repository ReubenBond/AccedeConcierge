import React from 'react';
import { BrowserRouter, Routes, Route, Link } from 'react-router-dom';
import App from './components/App';
import AdminPage from './components/AdminPage';
import './styles/Navigation.css';

const Navigation = () => (
    <nav className="main-nav">
        <div className="nav-container">
            <div className="nav-brand">Accede Travel</div>
            <ul className="nav-links">
                <li><Link to="/">Concierge</Link></li>
                <li><Link to="/admin">Admin</Link></li>
            </ul>
        </div>
    </nav>
);

const AppRouter: React.FC = () => (
    <BrowserRouter>
        <Navigation />
        <Routes>
            <Route path="/" element={<App />} />
            <Route path="/admin" element={<AdminPage />} />
        </Routes>
    </BrowserRouter>
);

export default AppRouter;
