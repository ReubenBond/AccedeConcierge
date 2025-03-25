import React from 'react';
import { Chat } from '../types/ChatTypes';

interface SidebarProps {
    chats: Chat[];
    selectedChatId: string | null;
    loadingChats: boolean;
    handleDeleteChat: (e: React.MouseEvent, chatId: string) => void;
    onSelectChat: (id: string) => void;
}

const Sidebar: React.FC<SidebarProps> = ({
    chats,
    selectedChatId,
    loadingChats,
    handleDeleteChat,
    onSelectChat
}) => {
    // Get background color based on issue status
    const getStatusBackgroundColor = (status: string): string => {
        switch (status) {
            case 'Pending':
                return '#1a2633'; // Dark blue tint for pending
            case 'Ready':
                return '#1b2a22'; // Dark green tint for ready
            default:
                return 'transparent';
        }
    };

    // Get border color based on issue type
    const getTypeBorderColor = (type: string): string => {
        switch (type) {
            case 'Question':
                return '#9c27b0'; // Purple for questions
            case 'Discussion':
                return '#2196f3'; // Blue for discussions
            case 'BugReport':
                return '#f44336'; // Red for bugs
            case 'Task':
                return '#ff9800'; // Orange for tasks
            case 'FeatureRequest':
                return '#4caf50'; // Green for features
            case 'OffTopic':
                return '#9e9e9e'; // Gray for off-topic
            case 'Unknown':
            default:
                return '#78909c'; // Blue-gray for unknown
        }
    };

    // Get text badge color based on issue type
    const getTypeBadgeStyle = (type: string): React.CSSProperties => {
        const baseStyle: React.CSSProperties = {
            display: 'inline-block',
            padding: '2px 6px',
            borderRadius: '10px',
            fontSize: '10px',
            fontWeight: 'bold',
            marginRight: '4px'
        };

        switch (type) {
            case 'Question':
                return { ...baseStyle, backgroundColor: '#9c27b066', color: '#e1bee7' };
            case 'Discussion':
                return { ...baseStyle, backgroundColor: '#2196f366', color: '#bbdefb' };
            case 'BugReport':
                return { ...baseStyle, backgroundColor: '#f4433666', color: '#ffcdd2' };
            case 'Task':
                return { ...baseStyle, backgroundColor: '#ff980066', color: '#ffe0b2' };
            case 'FeatureRequest':
                return { ...baseStyle, backgroundColor: '#4caf5066', color: '#c8e6c9' };
            case 'OffTopic':
                return { ...baseStyle, backgroundColor: '#9e9e9e66', color: '#f5f5f5' };
            case 'Unknown':
            default:
                return { ...baseStyle, backgroundColor: '#78909c66', color: '#cfd8dc' };
        }
    };

    // Map issue type to a more human-friendly display name
    const getDisplayTypeLabel = (type: string): string => {
        switch (type) {
            case 'BugReport':
                return 'Bug';
            case 'FeatureRequest':
                return 'Feature request';
            case 'OffTopic':
                return 'Off-topic';
            default:
                return type;
        }
    };

    const handleAddNewIssue = async () => {
        try {
            await fetch('/api/issues/load-more', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    targetIssueCount: chats.length + 1
                })
            });
        } catch (error) {
            console.error('Failed to request new issue:', error);
        }
    };

    return (
        <div className="sidebar" style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
            <div className="sidebar-header">
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <h2 style={{ display: 'flex', alignItems: 'center' }}>
                        <svg 
                            aria-hidden="true" 
                            width="24" 
                            height="24" 
                            viewBox="0 0 16 16" 
                            fill="currentColor" 
                            style={{ marginRight: '8px' }}
                        >
                            <path fillRule="evenodd" d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z" />
                        </svg>
                        &nbsp;Triage Mate
                    </h2>
                </div>
            </div>
            <div style={{ flexGrow: 1, overflowY: 'auto', marginBottom: '8px' }}>
                <ul className="chat-list">
                    {loadingChats && (
                        <li className="loading-message">
                            Loading...
                        </li>
                    )}
                    {chats.length === 0 && !loadingChats && (
                        <li className="no-issues-message">
                            No active issues found
                        </li>
                    )}
                    {chats.map(chat => (
                        <li
                            key={chat.id}
                            onClick={() => onSelectChat(chat.id)}
                            className={`chat-item ${selectedChatId === chat.id ? 'selected' : ''}`}
                            style={{
                                backgroundColor: selectedChatId === chat.id ? undefined : getStatusBackgroundColor(chat.issue.status),
                                borderLeft: `3px solid ${getTypeBorderColor(chat.issue.type)}`,
                                transition: 'all 0.2s ease',
                                position: 'relative'
                            }}
                        >
                            <div className="chat-name" style={{ 
                                fontSize: '14px', 
                                fontWeight: 'medium',
                                marginBottom: '20px'
                            }}>
                                {chat.issue.title}
                            </div>
                            <div style={{ 
                                display: 'flex',
                                gap: '4px',
                                position: 'absolute', 
                                bottom: '6px', 
                                left: '8px' 
                            }}>
                                <span style={getTypeBadgeStyle(chat.issue.type)}>
                                    {getDisplayTypeLabel(chat.issue.type)}
                                </span>
                                <span className="issue-number-badge">
                                    #{chat.issue.id.issueNumber}
                                </span>
                            </div>
                            <button
                                className="delete-chat-button"
                                onClick={(e) => handleDeleteChat(e, chat.id)}
                                title="Delete issue chat"
                            >
                                ×
                            </button>
                        </li>
                    ))}
                </ul>
            </div>
            <div style={{ flexShrink: 0 }}>
                <button 
                    onClick={handleAddNewIssue}
                    disabled={loadingChats}
                    style={{
                        padding: '6px 12px',
                        background: '#2ea44f',
                        color: 'white',
                        border: 'none',
                        borderRadius: '6px',
                        cursor: 'pointer',
                        fontSize: '14px',
                        width: '100%',
                        marginBottom: '16px'
                    }}
                    title="Add new issue"
                >
                    + Add More
                </button>
                
                <div className="powered-by" style={{ marginTop: '16px', fontSize: '12px' }}>
                    <p style={{ marginBottom: '8px', color: '#6e7781' }}>Made with</p>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                        {/* .NET Aspire Logo */}
                        <a 
                            href="https://github.com/dotnet/aspire/"
                            target="_blank"
                            rel="noopener noreferrer"
                            style={{ 
                                display: 'flex', 
                                alignItems: 'center',
                                textDecoration: 'none',
                                color: 'inherit'
                            }}
                        >
                            <img 
                                src="https://raw.githubusercontent.com/dotnet/docs-aspire/refs/heads/main/assets/dotnet-aspire-logo-64.svg" 
                                alt=".NET Aspire logo"
                                width="24"
                                height="24"
                                style={{ marginRight: '6px' }}
                            />
                            <span style={{ fontSize: '10px' }}>.NET Aspire</span>
                        </a>
                        
                        {/* Microsoft Orleans Logo */}
                        <a 
                            href="https://github.com/dotnet/orleans/"
                            target="_blank"
                            rel="noopener noreferrer"
                            style={{ 
                                display: 'flex', 
                                alignItems: 'center',
                                textDecoration: 'none',
                                color: 'inherit'
                            }}
                        >
                            <img 
                                src="https://raw.githubusercontent.com/dotnet/orleans/main/assets/logo_128.png" 
                                alt="Microsoft Orleans logo"
                                width="24"
                                height="24"
                                style={{ marginRight: '6px' }}
                            />
                            <span style={{ fontSize: '10px' }}>Microsoft Orleans</span>
                        </a>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default Sidebar;
