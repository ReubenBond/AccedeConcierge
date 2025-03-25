import React from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import App from './components/App';

const AppRouter: React.FC = () => (
    <BrowserRouter>
        <Routes>
            <Route path="/issue/:owner/:repository/:issueNumber" element={<App />} />
            <Route path="/" element={<App />} />
        </Routes>
    </BrowserRouter>
);

export default AppRouter;
